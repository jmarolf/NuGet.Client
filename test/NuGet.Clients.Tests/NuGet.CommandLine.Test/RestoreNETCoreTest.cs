﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using Newtonsoft.Json.Linq;
using NuGet.Frameworks;
using NuGet.Packaging;
using NuGet.Packaging.Core;
using NuGet.ProjectModel;
using NuGet.Test.Utility;
using NuGet.Versioning;
using Xunit;

namespace NuGet.CommandLine.Test
{
    public class RestoreNetCoreTest
    {
        [Fact]
        public async Task RestoreNetCore_SingleToolRestore()
        {
            // Arrange
            using (var pathContext = new SimpleTestPathContext())
            {
                // Set up solution, project, and packages
                var solution = new SimpleTestSolutionContext(pathContext.SolutionRoot);

                var projectA = SimpleTestProjectContext.CreateNETCore(
                    "a",
                    pathContext.SolutionRoot,
                    NuGetFramework.Parse("net45"));

                var packageX = new SimpleTestPackageContext()
                {
                    Id = "x",
                    Version = "1.0.0"
                };

                projectA.AddPackageToAllFrameworks(packageX);

                projectA.DotnetCLIToolReferences.Add(new SimpleTestPackageContext()
                {
                    Id = "z",
                    Version = "1.0.0"
                });

                solution.Projects.Add(projectA);
                solution.Create(pathContext.SolutionRoot);

                await SimpleTestPackageUtility.CreateFolderFeedV3(
                    pathContext.PackageSource,
                    PackageSaveMode.Defaultv3,
                    packageX);

                var path = Path.Combine(pathContext.UserPackagesFolder, ".tools", "z", "1.0.0", "project.lock.json");

                // Act
                var r = RestoreSolution(pathContext);

                // Assert
                Assert.True(File.Exists(path), r.Item2);
            }
        }

        [Fact]
        public async Task RestoreNetCore_VerifyBuildCrossTargeting_VerifyImportOrder()
        {
            // Arrange
            using (var pathContext = new SimpleTestPathContext())
            {
                // Set up solution, project, and packages
                var solution = new SimpleTestSolutionContext(pathContext.SolutionRoot);

                var projectA = SimpleTestProjectContext.CreateNETCore(
                    "a",
                    pathContext.SolutionRoot,
                    NuGetFramework.Parse("net45"));

                var packageX = new SimpleTestPackageContext()
                {
                    Id = "x",
                    Version = "1.0.0"
                };

                var packageZ = new SimpleTestPackageContext()
                {
                    Id = "z",
                    Version = "1.0.0"
                };

                var packageS = new SimpleTestPackageContext()
                {
                    Id = "s",
                    Version = "1.0.0"
                };

                packageX.AddFile("buildCrossTargeting/x.targets");
                packageZ.AddFile("buildCrossTargeting/z.targets");
                packageS.AddFile("buildCrossTargeting/s.targets");

                packageX.AddFile("buildCrossTargeting/x.props");
                packageZ.AddFile("buildCrossTargeting/z.props");
                packageS.AddFile("buildCrossTargeting/s.props");

                packageX.AddFile("build/x.targets");
                packageZ.AddFile("build/z.targets");
                packageS.AddFile("build/s.targets");

                packageX.AddFile("build/x.props");
                packageZ.AddFile("build/z.props");
                packageS.AddFile("build/s.props");

                packageX.AddFile("lib/net45/test.dll");
                packageX.AddFile("lib/net45/test.dll");
                packageX.AddFile("lib/net45/test.dll");

                // To avoid sorting on case accidently
                // A -> X -> Z -> S
                packageX.Dependencies.Add(packageZ);
                packageZ.Dependencies.Add(packageS);

                projectA.AddPackageToAllFrameworks(packageX);
                projectA.AddPackageToAllFrameworks(packageZ);
                projectA.AddPackageToAllFrameworks(packageS);

                solution.Projects.Add(projectA);
                solution.Create(pathContext.SolutionRoot);

                await SimpleTestPackageUtility.CreateFolderFeedV3(
                    pathContext.PackageSource,
                    PackageSaveMode.Defaultv3,
                    packageX);

                // Act
                var r = RestoreSolution(pathContext);

                // Assert
                Assert.True(File.Exists(projectA.TargetsOutput), r.Item2);

                var targetsXML = XDocument.Parse(File.ReadAllText(projectA.TargetsOutput));
                var targetItemGroups = targetsXML.Root.Elements().Where(e => e.Name.LocalName == "ImportGroup").ToList();

                var propsXML = XDocument.Parse(File.ReadAllText(projectA.PropsOutput));
                var propsItemGroups = propsXML.Root.Elements().Where(e => e.Name.LocalName == "ImportGroup").ToList();

                // CrossTargeting should be first
                Assert.Equal(2, targetItemGroups.Count);
                Assert.Equal("'$(IsCrossTargetingBuild)' == 'true'", targetItemGroups[0].Attribute(XName.Get("Condition")).Value.Trim());
                Assert.Equal("'$(TargetFramework)' == 'net45'", targetItemGroups[1].Attribute(XName.Get("Condition")).Value.Trim());

                Assert.Equal(3, targetItemGroups[0].Elements().Count());
                Assert.EndsWith("s.targets", targetItemGroups[0].Elements().ToList()[0].Attribute(XName.Get("Project")).Value);
                Assert.EndsWith("z.targets", targetItemGroups[0].Elements().ToList()[1].Attribute(XName.Get("Project")).Value);
                Assert.EndsWith("x.targets", targetItemGroups[0].Elements().ToList()[2].Attribute(XName.Get("Project")).Value);

                Assert.Equal(3, targetItemGroups[1].Elements().Count());
                Assert.EndsWith("s.targets", targetItemGroups[1].Elements().ToList()[0].Attribute(XName.Get("Project")).Value);
                Assert.EndsWith("z.targets", targetItemGroups[1].Elements().ToList()[1].Attribute(XName.Get("Project")).Value);
                Assert.EndsWith("x.targets", targetItemGroups[1].Elements().ToList()[2].Attribute(XName.Get("Project")).Value);

                Assert.Equal(2, propsItemGroups.Count);
                Assert.Equal("'$(IsCrossTargetingBuild)' == 'true'", propsItemGroups[0].Attribute(XName.Get("Condition")).Value.Trim());
                Assert.Equal("'$(TargetFramework)' == 'net45'", propsItemGroups[1].Attribute(XName.Get("Condition")).Value.Trim());

                Assert.Equal(3, propsItemGroups[0].Elements().Count());
                Assert.EndsWith("s.props", propsItemGroups[0].Elements().ToList()[0].Attribute(XName.Get("Project")).Value);
                Assert.EndsWith("z.props", propsItemGroups[0].Elements().ToList()[1].Attribute(XName.Get("Project")).Value);
                Assert.EndsWith("x.props", propsItemGroups[0].Elements().ToList()[2].Attribute(XName.Get("Project")).Value);

                Assert.Equal(3, propsItemGroups[1].Elements().Count());
                Assert.EndsWith("s.props", propsItemGroups[1].Elements().ToList()[0].Attribute(XName.Get("Project")).Value);
                Assert.EndsWith("z.props", propsItemGroups[1].Elements().ToList()[1].Attribute(XName.Get("Project")).Value);
                Assert.EndsWith("x.props", propsItemGroups[1].Elements().ToList()[2].Attribute(XName.Get("Project")).Value);
            }
        }

        [Fact]
        public async Task RestoreNetCore_VerifyBuildCrossTargeting_VerifyImportIsAdded()
        {
            // Arrange
            using (var pathContext = new SimpleTestPathContext())
            {
                // Set up solution, project, and packages
                var solution = new SimpleTestSolutionContext(pathContext.SolutionRoot);

                var projectA = SimpleTestProjectContext.CreateNETCore(
                    "a",
                    pathContext.SolutionRoot,
                    NuGetFramework.Parse("net45"));

                var packageX = new SimpleTestPackageContext()
                {
                    Id = "x",
                    Version = "1.0.0"
                };

                packageX.AddFile("buildCrossTargeting/x.targets");
                packageX.AddFile("lib/net45/test.dll");

                projectA.AddPackageToAllFrameworks(packageX);

                solution.Projects.Add(projectA);
                solution.Create(pathContext.SolutionRoot);

                await SimpleTestPackageUtility.CreateFolderFeedV3(
                    pathContext.PackageSource,
                    PackageSaveMode.Defaultv3,
                    packageX);

                // Act
                var r = RestoreSolution(pathContext);

                // Assert
                Assert.True(File.Exists(projectA.TargetsOutput), r.Item2);

                var targetsXML = XDocument.Parse(File.ReadAllText(projectA.TargetsOutput));
                var targetItemGroups = targetsXML.Root.Elements().Where(e => e.Name.LocalName == "ImportGroup").ToList();

                Assert.Equal(1, targetItemGroups.Count);
                Assert.Equal("'$(IsCrossTargetingBuild)' == 'true'", targetItemGroups[0].Attribute(XName.Get("Condition")).Value.Trim());
                Assert.Equal(1, targetItemGroups[0].Elements().Count());
                Assert.EndsWith("x.targets", targetItemGroups[0].Elements().ToList()[0].Attribute(XName.Get("Project")).Value);
            }
        }

        [Fact]
        public async Task RestoreNetCore_VerifyBuildCrossTargeting_VerifyImportIsNotAddedForUAP()
        {
            // Arrange
            using (var pathContext = new SimpleTestPathContext())
            {
                // Set up solution, project, and packages
                var solution = new SimpleTestSolutionContext(pathContext.SolutionRoot);

                var projectJson = JObject.Parse(@"{
                                                    'dependencies': {
                                                    },
                                                    'frameworks': {
                                                        'net45': {
                                                            'x': '1.0.0'
                                                    }
                                                  }
                                               }");

                var projectA = SimpleTestProjectContext.CreateUAP(
                    "a",
                    pathContext.SolutionRoot,
                    NuGetFramework.Parse("net45"),
                    projectJson);

                var packageX = new SimpleTestPackageContext()
                {
                    Id = "x",
                    Version = "1.0.0"
                };

                packageX.AddFile("buildCrossTargeting/x.targets");
                packageX.AddFile("lib/net45/test.dll");

                projectA.AddPackageToAllFrameworks(packageX);

                solution.Projects.Add(projectA);
                solution.Create(pathContext.SolutionRoot);

                await SimpleTestPackageUtility.CreateFolderFeedV3(
                    pathContext.PackageSource,
                    PackageSaveMode.Defaultv3,
                    packageX);

                // Act
                var r = RestoreSolution(pathContext);

                // Assert
                Assert.False(File.Exists(projectA.TargetsOutput), r.Item2);
            }
        }

        [Fact]
        public async Task RestoreNetCore_VerifyBuildCrossTargeting_VerifyImportRequiresPackageName()
        {
            // Arrange
            using (var pathContext = new SimpleTestPathContext())
            {
                // Set up solution, project, and packages
                var solution = new SimpleTestSolutionContext(pathContext.SolutionRoot);

                var projectA = SimpleTestProjectContext.CreateNETCore(
                    "a",
                    pathContext.SolutionRoot,
                    NuGetFramework.Parse("net45"));

                var packageX = new SimpleTestPackageContext()
                {
                    Id = "x",
                    Version = "1.0.0"
                };

                packageX.AddFile("buildCrossTargeting/a.targets");
                packageX.AddFile("buildCrossTargeting/a.props");
                packageX.AddFile("buildCrossTargeting/a.txt");
                packageX.AddFile("lib/net45/test.dll");

                projectA.AddPackageToAllFrameworks(packageX);

                solution.Projects.Add(projectA);
                solution.Create(pathContext.SolutionRoot);

                await SimpleTestPackageUtility.CreateFolderFeedV3(
                    pathContext.PackageSource,
                    PackageSaveMode.Defaultv3,
                    packageX);

                // Act
                var r = RestoreSolution(pathContext);

                // Assert
                Assert.False(File.Exists(projectA.TargetsOutput), r.Item2);
                Assert.False(File.Exists(projectA.PropsOutput), r.Item2);
            }
        }

        [Fact]
        public async Task RestoreNetCore_VerifyBuildCrossTargeting_VerifyImportNotAllowedInSubFolder()
        {
            // Arrange
            using (var pathContext = new SimpleTestPathContext())
            {
                // Set up solution, project, and packages
                var solution = new SimpleTestSolutionContext(pathContext.SolutionRoot);

                var projectA = SimpleTestProjectContext.CreateNETCore(
                    "a",
                    pathContext.SolutionRoot,
                    NuGetFramework.Parse("net45"));

                var packageX = new SimpleTestPackageContext()
                {
                    Id = "x",
                    Version = "1.0.0"
                };

                var packageY = new SimpleTestPackageContext()
                {
                    Id = "y",
                    Version = "1.0.0"
                };

                packageX.AddFile("buildCrossTargeting/net45/x.targets");
                packageX.AddFile("buildCrossTargeting/net45/x.props");
                packageX.AddFile("lib/net45/test.dll");

                packageY.AddFile("buildCrossTargeting/a.targets");
                packageY.AddFile("buildCrossTargeting/a.props");
                packageY.AddFile("buildCrossTargeting/net45/y.targets");
                packageY.AddFile("buildCrossTargeting/net45/y.props");
                packageY.AddFile("lib/net45/test.dll");

                projectA.AddPackageToAllFrameworks(packageX);
                projectA.AddPackageToAllFrameworks(packageY);

                solution.Projects.Add(projectA);
                solution.Create(pathContext.SolutionRoot);

                await SimpleTestPackageUtility.CreateFolderFeedV3(
                    pathContext.PackageSource,
                    PackageSaveMode.Defaultv3,
                    packageX,
                    packageY);

                // Act
                var r = RestoreSolution(pathContext);

                // Assert
                Assert.False(File.Exists(projectA.TargetsOutput), r.Item2);
                Assert.False(File.Exists(projectA.PropsOutput), r.Item2);
            }
        }

        [Fact]
        public async Task RestoreNetCore_ProjectToProject_Interweaving()
        {
            // Arrange
            using (var pathContext = new SimpleTestPathContext())
            {
                // Set up solution, project, and packages
                var solution = new SimpleTestSolutionContext(pathContext.SolutionRoot);

                //  A         B        C          D         E       F          G
                // NETCore -> UAP -> Unknown -> NETCore -> UAP -> Unknown -> NETCore

                var projectA = SimpleTestProjectContext.CreateNETCore(
                    "a",
                    pathContext.SolutionRoot,
                    NuGetFramework.Parse("net45"));

                var projectJson = JObject.Parse(@"{
                                                    'dependencies': {
                                                    },
                                                    'frameworks': {
                                                        'net45': { }
                                                  }
                                               }");

                var projectB = SimpleTestProjectContext.CreateUAP(
                    "b",
                    pathContext.SolutionRoot,
                    NuGetFramework.Parse("net45"),
                    projectJson);

                var projectC = SimpleTestProjectContext.CreateNonNuGet(
                    "c",
                    pathContext.SolutionRoot,
                    NuGetFramework.Parse("net45"));

                var projectD = SimpleTestProjectContext.CreateNETCore(
                    "d",
                    pathContext.SolutionRoot,
                    NuGetFramework.Parse("net45"));

                var projectE = SimpleTestProjectContext.CreateUAP(
                    "e",
                    pathContext.SolutionRoot,
                    NuGetFramework.Parse("net45"),
                    projectJson);

                var projectF = SimpleTestProjectContext.CreateNonNuGet(
                    "f",
                    pathContext.SolutionRoot,
                    NuGetFramework.Parse("net45"));

                var projectG = SimpleTestProjectContext.CreateNETCore(
                    "g",
                    pathContext.SolutionRoot,
                    NuGetFramework.Parse("net45"));

                // Link everything
                projectA.AddProjectToAllFrameworks(projectB);
                projectB.AddProjectToAllFrameworks(projectC);
                projectC.AddProjectToAllFrameworks(projectD);
                projectD.AddProjectToAllFrameworks(projectE);
                projectE.AddProjectToAllFrameworks(projectF);
                projectF.AddProjectToAllFrameworks(projectG);

                var packageX = new SimpleTestPackageContext()
                {
                    Id = "x",
                    Version = "1.0.0-beta"
                };
               
                // G -> X
                projectG.AddPackageToAllFrameworks(packageX);

                await SimpleTestPackageUtility.CreateFolderFeedV3(
                    pathContext.PackageSource,
                    PackageSaveMode.Defaultv3,
                    packageX);

                solution.Projects.Add(projectA);
                solution.Projects.Add(projectB);
                solution.Projects.Add(projectC);
                solution.Projects.Add(projectD);
                solution.Projects.Add(projectE);
                solution.Projects.Add(projectF);
                solution.Projects.Add(projectG);
                solution.Create(pathContext.SolutionRoot);

                // Act
                var r = RestoreSolution(pathContext);

                // Assert
                var targets = projectA.AssetsFile.Targets.Single().Libraries.ToDictionary(e => e.Name);
                var libs = projectA.AssetsFile.Libraries.ToDictionary(e => e.Name);

                // Verify everything showed up
                Assert.Equal(new[] { "b", "c", "d", "e", "f", "g", "x" }, libs.Keys.OrderBy(s => s, StringComparer.OrdinalIgnoreCase));

                Assert.Equal("1.0.0-beta", targets["x"].Version.ToNormalizedString());
                Assert.Equal("package", targets["x"].Type);

                Assert.Equal("1.0.0", targets["g"].Version.ToNormalizedString());
                Assert.Equal("project", targets["g"].Type);
                Assert.Equal(".NETFramework,Version=v4.5", targets["g"].Framework);

                Assert.Equal("1.0.0", libs["g"].Version.ToNormalizedString());
                Assert.Equal("project", libs["g"].Type);
                Assert.Equal("../g/g.csproj", libs["g"].MSBuildProject);
                Assert.Equal("../g/g.csproj", libs["g"].Path);
            }
        }

        [Fact]
        public void RestoreNetCore_ProjectToProject_NETCoreToUnknown()
        {
            // Arrange
            using (var pathContext = new SimpleTestPathContext())
            {
                // Set up solution, project, and packages
                var solution = new SimpleTestSolutionContext(pathContext.SolutionRoot);

                var projectA = SimpleTestProjectContext.CreateNETCore(
                    "a",
                    pathContext.SolutionRoot,
                    NuGetFramework.Parse("net45"));

                var projectB = SimpleTestProjectContext.CreateNonNuGet(
                    "b",
                    pathContext.SolutionRoot,
                    NuGetFramework.Parse("net45"));

                // A -> B
                projectA.AddProjectToAllFrameworks(projectB);

                solution.Projects.Add(projectA);
                solution.Projects.Add(projectB);
                solution.Create(pathContext.SolutionRoot);

                // Act
                var r = RestoreSolution(pathContext);

                // Assert
                var targetB = projectA.AssetsFile.Targets.Single().Libraries.SingleOrDefault(e => e.Name == "b");
                var libB = projectA.AssetsFile.Libraries.SingleOrDefault(e => e.Name == "b");

                Assert.Equal("1.0.0", targetB.Version.ToNormalizedString());
                Assert.Equal("project", targetB.Type);

                // This is not populated for unknown project types, but this may change in the future.
                Assert.Null(targetB.Framework);
                Assert.Null(libB.Path);

                Assert.Equal("1.0.0", libB.Version.ToNormalizedString());
                Assert.Equal("project", libB.Type);
                Assert.Equal("../b/b.csproj", libB.MSBuildProject);
            }
        }

        [Fact]
        public void RestoreNetCore_ProjectToProject_NETCoreToUAP()
        {
            // Arrange
            using (var pathContext = new SimpleTestPathContext())
            {
                // Set up solution, project, and packages
                var solution = new SimpleTestSolutionContext(pathContext.SolutionRoot);

                var projectA = SimpleTestProjectContext.CreateNETCore(
                    "a",
                    pathContext.SolutionRoot,
                    NuGetFramework.Parse("net45"));

                var projectJson = JObject.Parse(@"{
                                                    'dependencies': {
                                                    },
                                                    'frameworks': {
                                                        'net45': { }
                                                  }
                                               }");

                var projectB = SimpleTestProjectContext.CreateUAP(
                    "b",
                    pathContext.SolutionRoot,
                    NuGetFramework.Parse("net45"),
                    projectJson);

                // A -> B
                projectA.AddProjectToAllFrameworks(projectB);

                solution.Projects.Add(projectA);
                solution.Projects.Add(projectB);
                solution.Create(pathContext.SolutionRoot);

                // Act
                var r = RestoreSolution(pathContext);

                // Assert
                var targetB = projectA.AssetsFile.Targets.Single().Libraries.SingleOrDefault(e => e.Name == "b");
                var libB = projectA.AssetsFile.Libraries.SingleOrDefault(e => e.Name == "b");

                Assert.Equal("1.0.0", targetB.Version.ToNormalizedString());
                Assert.Equal("project", targetB.Type);
                Assert.Equal(".NETFramework,Version=v4.5", targetB.Framework);

                Assert.Equal("1.0.0", libB.Version.ToNormalizedString());
                Assert.Equal("project", libB.Type);
                Assert.Equal("../b/b.csproj", libB.MSBuildProject);
                Assert.Equal("../b/project.json", libB.Path); // TODO: is this right?
            }
        }

        [Fact]
        public async Task RestoreNetCore_ProjectToProject_UAPToNetCore()
        {
            // Arrange
            using (var pathContext = new SimpleTestPathContext())
            {
                // Set up solution, project, and packages
                var solution = new SimpleTestSolutionContext(pathContext.SolutionRoot);

                var projectJson = JObject.Parse(@"{
                                                    'dependencies': {
                                                    },
                                                    'frameworks': {
                                                        'UAP10.0': { }
                                                  }
                                               }");

                var projectA = SimpleTestProjectContext.CreateUAP(
                    "a",
                    pathContext.SolutionRoot,
                    NuGetFramework.Parse("UAP10.0"),
                    projectJson);

                var projectB = SimpleTestProjectContext.CreateNETCore(
                    "b",
                    pathContext.SolutionRoot,
                    NuGetFramework.Parse("netstandard1.3"),
                    NuGetFramework.Parse("net45"));

                var packageX = new SimpleTestPackageContext()
                {
                    Id = "x",
                    Version = "1.0.0"
                };

                var packageY = new SimpleTestPackageContext()
                {
                    Id = "y",
                    Version = "1.0.0"
                };

                await SimpleTestPackageUtility.CreateFolderFeedV3(
                    pathContext.PackageSource,
                    PackageSaveMode.Defaultv3,
                    packageX,
                    packageY);

                projectB.Frameworks[0].PackageReferences.Add(packageX);
                projectB.Frameworks[1].PackageReferences.Add(packageY);

                // A -> B
                projectA.AddProjectToAllFrameworks(projectB);

                solution.Projects.Add(projectA);
                solution.Projects.Add(projectB);
                solution.Create(pathContext.SolutionRoot);

                // Act
                var r = RestoreSolution(pathContext);

                // Assert
                var targetB = projectA.AssetsFile.Targets.Single(e => e.TargetFramework.Equals(NuGetFramework.Parse("UAP10.0"))).Libraries.SingleOrDefault(e => e.Name == "b");
                var libB = projectA.AssetsFile.Libraries.SingleOrDefault(e => e.Name == "b");

                var targetX = projectA.AssetsFile.Targets.Single().Libraries.SingleOrDefault(e => e.Name == "x");
                var targetY = projectA.AssetsFile.Targets.Single().Libraries.SingleOrDefault(e => e.Name == "y");

                Assert.Equal("1.0.0", targetB.Version.ToNormalizedString());
                Assert.Equal("project", targetB.Type);
                Assert.Equal(".NETStandard,Version=v1.3", targetB.Framework);

                Assert.Equal("1.0.0", libB.Version.ToNormalizedString());
                Assert.Equal("project", libB.Type);
                Assert.Equal("../b/b.csproj", libB.MSBuildProject);
                Assert.Equal("../b/b.csproj", libB.Path);

                Assert.Equal("1.0.0", targetX.Version.ToNormalizedString());
                Assert.Null(targetY);
            }
        }

        [Fact]
        public void  RestoreNetCore_ProjectToProject_UAPToUnknown()
        {
            // Arrange
            using (var pathContext = new SimpleTestPathContext())
            {
                // Set up solution, project, and packages
                var solution = new SimpleTestSolutionContext(pathContext.SolutionRoot);

                var projectJson = JObject.Parse(@"{
                                                    'dependencies': {
                                                    },
                                                    'frameworks': {
                                                        'UAP10.0': { }
                                                  }
                                               }");

                var projectA = SimpleTestProjectContext.CreateUAP(
                    "a",
                    pathContext.SolutionRoot,
                    NuGetFramework.Parse("UAP10.0"),
                    projectJson);

                var projectB = SimpleTestProjectContext.CreateNonNuGet(
                    "b",
                    pathContext.SolutionRoot,
                    NuGetFramework.Parse("netstandard1.3"));

                // A -> B
                projectA.AddProjectToAllFrameworks(projectB);

                solution.Projects.Add(projectA);
                solution.Projects.Add(projectB);
                solution.Create(pathContext.SolutionRoot);

                // Act
                var r = RestoreSolution(pathContext);

                // Assert
                var targetB = projectA.AssetsFile.Targets.Single(e => e.TargetFramework.Equals(NuGetFramework.Parse("UAP10.0"))).Libraries.SingleOrDefault(e => e.Name == "b");
                var libB = projectA.AssetsFile.Libraries.SingleOrDefault(e => e.Name == "b");

                Assert.Equal("1.0.0", targetB.Version.ToNormalizedString());
                Assert.Equal("project", targetB.Type);
                Assert.Null(targetB.Framework);

                Assert.Equal("1.0.0", libB.Version.ToNormalizedString());
                Assert.Equal("project", libB.Type);
                Assert.Equal("../b/b.csproj", libB.MSBuildProject);
                Assert.Null(libB.Path);
            }
        }

        [Fact]
        public void RestoreNetCore_ProjectToProject_UAPToUAP_RestoreCSProjDirect()
        {
            // Arrange
            using (var pathContext = new SimpleTestPathContext())
            {
                // Set up solution, project, and packages
                var solution = new SimpleTestSolutionContext(pathContext.SolutionRoot);

                var projectJsonA = JObject.Parse(@"{
                                                    'dependencies': {
                                                    },
                                                    'frameworks': {
                                                        'NETCoreApp1.0': { }
                                                  }
                                               }");

                var projectA = SimpleTestProjectContext.CreateUAP(
                    "a",
                    pathContext.SolutionRoot,
                    NuGetFramework.Parse("NETCoreApp1.0"),
                    projectJsonA);

                var projectJsonB = JObject.Parse(@"{
                                                    'dependencies': {
                                                    },
                                                    'frameworks': {
                                                        'netstandard1.5': { }
                                                  }
                                               }");

                var projectB = SimpleTestProjectContext.CreateUAP(
                    "b",
                    pathContext.SolutionRoot,
                    NuGetFramework.Parse("NETCoreApp1.0"),
                    projectJsonB);

                // A -> B
                projectA.AddProjectToAllFrameworks(projectB);

                solution.Projects.Add(projectA);
                solution.Projects.Add(projectB);
                solution.Create(pathContext.SolutionRoot);

                // Act
                var nugetexe = Util.GetNuGetExePath();

                // Store the dg file for debugging
                var dgPath = Path.Combine(pathContext.WorkingDirectory, "out.dg");
                var envVars = new Dictionary<string, string>()
                {
                    { "NUGET_PERSIST_DG", "true" },
                    { "NUGET_PERSIST_DG_PATH", dgPath }
                };

                string[] args = new string[] {
                    "restore",
                    projectA.ProjectPath,
                    "-Verbosity",
                    "detailed"
                };

                // Act
                var r = CommandRunner.Run(
                    nugetexe,
                    pathContext.WorkingDirectory.Path,
                    string.Join(" ", args),
                    waitForExit: true,
                    environmentVariables: envVars);

                // Assert
                Assert.True(0 == r.Item1, r.Item2 + " " + r.Item3);

                // Assert
                var targetB = projectA.AssetsFile.Targets.Single(e => e.TargetFramework.Equals(NuGetFramework.Parse("NETCoreApp1.0"))).Libraries.SingleOrDefault(e => e.Name == "b");
                var libB = projectA.AssetsFile.Libraries.SingleOrDefault(e => e.Name == "b");

                Assert.Equal("1.0.0", targetB.Version.ToNormalizedString());
                Assert.Equal("project", targetB.Type);
                Assert.Equal(NuGetFramework.Parse("netstandard1.5"), NuGetFramework.Parse(targetB.Framework));

                Assert.Equal("1.0.0", libB.Version.ToNormalizedString());
                Assert.Equal("project", libB.Type);
                Assert.Equal("../b/b.csproj", libB.MSBuildProject);
                Assert.Equal("../b/project.json", libB.Path);
            }
        }

        [Fact]
        public void RestoreNetCore_ProjectToProject_NETCoreToNETCore_RestoreCSProjDirect()
        {
            // Arrange
            using (var pathContext = new SimpleTestPathContext())
            {
                // Set up solution, project, and packages
                var solution = new SimpleTestSolutionContext(pathContext.SolutionRoot);

                var projectA = SimpleTestProjectContext.CreateNETCore(
                    "a",
                    pathContext.SolutionRoot,
                    NuGetFramework.Parse("NETCoreApp1.0"));

                var projectB = SimpleTestProjectContext.CreateNETCore(
                    "b",
                    pathContext.SolutionRoot,
                    NuGetFramework.Parse("NETStandard1.5"));

                // A -> B
                projectA.AddProjectToAllFrameworks(projectB);

                solution.Projects.Add(projectA);
                solution.Projects.Add(projectB);
                solution.Create(pathContext.SolutionRoot);

                // Act
                var nugetexe = Util.GetNuGetExePath();

                // Store the dg file for debugging
                var dgPath = Path.Combine(pathContext.WorkingDirectory, "out.dg");
                var envVars = new Dictionary<string, string>()
                {
                    { "NUGET_PERSIST_DG", "true" },
                    { "NUGET_PERSIST_DG_PATH", dgPath }
                };

                string[] args = new string[] {
                    "restore",
                    projectA.ProjectPath,
                    "-Verbosity",
                    "detailed"
                };

                // Act
                var r = CommandRunner.Run(
                    nugetexe,
                    pathContext.WorkingDirectory.Path,
                    string.Join(" ", args),
                    waitForExit: true,
                    environmentVariables: envVars);

                // Assert
                Assert.True(0 == r.Item1, r.Item2 + " " + r.Item3);

                // Assert
                var targetB = projectA.AssetsFile.Targets.Single(e => e.TargetFramework.Equals(NuGetFramework.Parse("NETCoreApp1.0"))).Libraries.SingleOrDefault(e => e.Name == "b");
                var libB = projectA.AssetsFile.Libraries.SingleOrDefault(e => e.Name == "b");

                Assert.Equal("1.0.0", targetB.Version.ToNormalizedString());
                Assert.Equal("project", targetB.Type);
                Assert.Equal(NuGetFramework.Parse("netstandard1.5"), NuGetFramework.Parse(targetB.Framework));

                Assert.Equal("1.0.0", libB.Version.ToNormalizedString());
                Assert.Equal("project", libB.Type);
                Assert.Equal("../b/b.csproj", libB.MSBuildProject);
                Assert.Equal("../b/b.csproj", libB.Path);
            }
        }

        [Fact]
        public async Task RestoreNetCore_SingleProject()
        {
            // Arrange
            using (var pathContext = new SimpleTestPathContext())
            {
                // Set up solution, project, and packages
                var solution = new SimpleTestSolutionContext(pathContext.SolutionRoot);

                var projectA = SimpleTestProjectContext.CreateNETCore(
                    "a",
                    pathContext.SolutionRoot,
                    NuGetFramework.Parse("net45"));

                var packageX = new SimpleTestPackageContext()
                {
                    Id = "x",
                    Version = "1.0.0"
                };

                projectA.AddPackageToAllFrameworks(packageX);

                solution.Projects.Add(projectA);
                solution.Create(pathContext.SolutionRoot);

                await SimpleTestPackageUtility.CreateFolderFeedV3(
                    pathContext.PackageSource,
                    PackageSaveMode.Defaultv3,
                    packageX);

                // Act
                var r = RestoreSolution(pathContext);

                // Assert
                Assert.True(File.Exists(projectA.AssetsFileOutputPath), r.Item2);
                Assert.True(File.Exists(projectA.TargetsOutput), r.Item2);
                Assert.False(File.Exists(projectA.PropsOutput), r.Item2);
            }
        }

        [Fact]
        public async Task RestoreNetCore_SingleProject_SingleTFM()
        {
            // Arrange
            using (var pathContext = new SimpleTestPathContext())
            {
                // Set up solution, project, and packages
                var solution = new SimpleTestSolutionContext(pathContext.SolutionRoot);

                var projectA = SimpleTestProjectContext.CreateNETCore(
                    "a",
                    pathContext.SolutionRoot,
                    NuGetFramework.Parse("net45"));

                var packageX = new SimpleTestPackageContext()
                {
                    Id = "x",
                    Version = "1.0.0"
                };

                projectA.AddPackageToAllFrameworks(packageX);

                solution.Projects.Add(projectA);
                solution.Create(pathContext.SolutionRoot);

                var xml = File.ReadAllText(projectA.ProjectPath);
                xml = xml.Replace("<TargetFrameworks>", "<TargetFramework>");
                xml = xml.Replace("</TargetFrameworks>", "</TargetFramework>");
                File.WriteAllText(projectA.ProjectPath, xml);

                await SimpleTestPackageUtility.CreateFolderFeedV3(
                    pathContext.PackageSource,
                    PackageSaveMode.Defaultv3,
                    packageX);

                // Act
                var r = RestoreSolution(pathContext);

                // Assert
                Assert.True(File.Exists(projectA.AssetsFileOutputPath), r.Item2);
                Assert.True(File.Exists(projectA.TargetsOutput), r.Item2);
                Assert.False(File.Exists(projectA.PropsOutput), r.Item2);

                Assert.Equal(NuGetFramework.Parse("net45"), projectA.AssetsFile.Targets.Single().TargetFramework);
            }
        }

        [Fact]
        public void RestoreNetCore_NETCore_ProjectToProject_VerifyProjectInTarget()
        {
            // Arrange
            using (var pathContext = new SimpleTestPathContext())
            {
                // Set up solution, project, and packages
                var solution = new SimpleTestSolutionContext(pathContext.SolutionRoot);

                var projectA = SimpleTestProjectContext.CreateNETCore(
                    "a",
                    pathContext.SolutionRoot,
                    NuGetFramework.Parse("net45"));

                var projectB = SimpleTestProjectContext.CreateNETCore(
                    "b",
                    pathContext.SolutionRoot,
                    NuGetFramework.Parse("net45"));

                // A -> B
                projectA.AddProjectToAllFrameworks(projectB);

                solution.Projects.Add(projectA);
                solution.Projects.Add(projectB);
                solution.Create(pathContext.SolutionRoot);

                // Act
                var r = RestoreSolution(pathContext);

                // Assert
                var targetB = projectA.AssetsFile.Targets.Single().Libraries.SingleOrDefault(e => e.Name == "b");
                var libB = projectA.AssetsFile.Libraries.SingleOrDefault(e => e.Name == "b");

                Assert.Equal("1.0.0", targetB.Version.ToNormalizedString());
                Assert.Equal("project", targetB.Type);
                Assert.Equal(".NETFramework,Version=v4.5", targetB.Framework);

                Assert.Equal("1.0.0", libB.Version.ToNormalizedString());
                Assert.Equal("project", libB.Type);
                Assert.Equal("../b/b.csproj", libB.Path);
                Assert.Equal("../b/b.csproj", libB.MSBuildProject);
            }
        }

        [Fact]
        public void RestoreNetCore_NETCore_ProjectToProject_MissingProjectReference()
        {
            // Arrange
            using (var pathContext = new SimpleTestPathContext())
            {
                // Set up solution, project, and packages
                var solution = new SimpleTestSolutionContext(pathContext.SolutionRoot);

                var projectA = SimpleTestProjectContext.CreateNETCore(
                    "a",
                    pathContext.SolutionRoot,
                    NuGetFramework.Parse("net45"));

                var projectB = SimpleTestProjectContext.CreateNETCore(
                    "b",
                    pathContext.SolutionRoot,
                    NuGetFramework.Parse("net45"));

                // A -> B
                projectA.AddProjectToAllFrameworks(projectB);

                solution.Projects.Add(projectA);
                solution.Projects.Add(projectB);
                solution.Create(pathContext.SolutionRoot);

                // Delete B
                File.Delete(projectB.ProjectPath);

                // Act && Assert
                var r = RestoreSolution(pathContext, exitCode: 1);
            }
        }

        [Fact]
        public async Task RestoreNetCore_NETCore_ProjectToProject_VerifyTransitivePackage()
        {
            // Arrange
            using (var pathContext = new SimpleTestPathContext())
            {
                // Set up solution, project, and packages
                var solution = new SimpleTestSolutionContext(pathContext.SolutionRoot);

                var projectA = SimpleTestProjectContext.CreateNETCore(
                    "a",
                    pathContext.SolutionRoot,
                    NuGetFramework.Parse("net45"));

                var projectB = SimpleTestProjectContext.CreateNETCore(
                    "b",
                    pathContext.SolutionRoot,
                    NuGetFramework.Parse("net45"));

                // A -> B
                projectA.AddProjectToAllFrameworks(projectB);

                var packageX = new SimpleTestPackageContext()
                {
                    Id = "x",
                    Version = "1.0.0"
                };

                // B -> X
                projectB.AddPackageToAllFrameworks(packageX);

                solution.Projects.Add(projectA);
                solution.Projects.Add(projectB);
                solution.Create(pathContext.SolutionRoot);

                await SimpleTestPackageUtility.CreateFolderFeedV3(
                    pathContext.PackageSource,
                    PackageSaveMode.Defaultv3,
                    packageX);

                // Act
                var r = RestoreSolution(pathContext);

                // Assert
                var targetX = projectA.AssetsFile.Targets.Single().Libraries.SingleOrDefault(e => e.Name == "x");

                Assert.Equal("1.0.0", targetX.Version.ToNormalizedString());
            }
        }

        [Fact]
        public async Task RestoreNetCore_NETCore_ProjectToProjectMultipleTFM_VerifyTransitivePackages()
        {
            // Arrange
            using (var pathContext = new SimpleTestPathContext())
            {
                // Set up solution, project, and packages
                var solution = new SimpleTestSolutionContext(pathContext.SolutionRoot);

                var projectA = SimpleTestProjectContext.CreateNETCore(
                    "a",
                    pathContext.SolutionRoot,
                    NuGetFramework.Parse("net46"),
                    NuGetFramework.Parse("netstandard1.6"));

                var projectB = SimpleTestProjectContext.CreateNETCore(
                    "b",
                    pathContext.SolutionRoot,
                    NuGetFramework.Parse("net45"),
                    NuGetFramework.Parse("netstandard1.3"));

                // A -> B
                projectA.AddProjectToAllFrameworks(projectB);

                var packageX = new SimpleTestPackageContext()
                {
                    Id = "x",
                    Version = "1.0.0"
                };

                var packageY = new SimpleTestPackageContext()
                {
                    Id = "y",
                    Version = "1.0.0"
                };

                // B -> X
                projectB.Frameworks[0].PackageReferences.Add(packageX);

                // B -> Y
                projectB.Frameworks[1].PackageReferences.Add(packageY);

                solution.Projects.Add(projectA);
                solution.Projects.Add(projectB);
                solution.Create(pathContext.SolutionRoot);

                await SimpleTestPackageUtility.CreateFolderFeedV3(
                    pathContext.PackageSource,
                    PackageSaveMode.Defaultv3,
                    packageX,
                    packageY);

                // Act
                var r = RestoreSolution(pathContext);

                // Assert
                var targetNet = projectA.AssetsFile.Targets.Single(e => e.TargetFramework.Equals(NuGetFramework.Parse("net46")));
                var targetNS = projectA.AssetsFile.Targets.Single(e => e.TargetFramework.Equals(NuGetFramework.Parse("netstandard1.6")));

                Assert.Equal("x", targetNet.Libraries.Single(e => e.Type == "package").Name);
                Assert.Equal("y", targetNS.Libraries.Single(e => e.Type == "package").Name);
            }
        }

        [Fact]
        public async Task RestoreNetCore_NETCoreAndUAP_ProjectToProjectMultipleTFM_VerifyTransitivePackages()
        {
            // Arrange
            using (var pathContext = new SimpleTestPathContext())
            {
                // Set up solution, project, and packages
                var solution = new SimpleTestSolutionContext(pathContext.SolutionRoot);

                var projectA = SimpleTestProjectContext.CreateNETCore(
                    "a",
                    pathContext.SolutionRoot,
                    NuGetFramework.Parse("net46"),
                    NuGetFramework.Parse("netstandard1.6"));

                var projectJson = JObject.Parse(@"{
                                                    'dependencies': {
                                                        'x': '1.0.0'
                                                    },
                                                    'frameworks': {
                                                        'netstandard1.3': { }
                                                  }
                                               }");

                var projectB = SimpleTestProjectContext.CreateUAP(
                    "b",
                    pathContext.SolutionRoot,
                    NuGetFramework.Parse("netstandard1.3"),
                    projectJson);

                // A -> B
                projectA.AddProjectToAllFrameworks(projectB);

                var packageX = new SimpleTestPackageContext()
                {
                    Id = "x",
                    Version = "1.0.0"
                };

                // B -> X
                projectB.Frameworks[0].PackageReferences.Add(packageX);

                solution.Projects.Add(projectA);
                solution.Projects.Add(projectB);
                solution.Create(pathContext.SolutionRoot);

                await SimpleTestPackageUtility.CreateFolderFeedV3(
                    pathContext.PackageSource,
                    PackageSaveMode.Defaultv3,
                    packageX);

                // Act
                var r = RestoreSolution(pathContext);

                // Assert
                var targetNet = projectA.AssetsFile.Targets.Single(e => e.TargetFramework.Equals(NuGetFramework.Parse("net46")));
                var targetNS = projectA.AssetsFile.Targets.Single(e => e.TargetFramework.Equals(NuGetFramework.Parse("netstandard1.6")));

                Assert.Equal("x", targetNet.Libraries.Single(e => e.Type == "package").Name);
                Assert.Equal("x", targetNS.Libraries.Single(e => e.Type == "package").Name);
            }
        }

        private static CommandRunnerResult RestoreSolution(SimpleTestPathContext pathContext, int exitCode=0)
        {
            var nugetexe = Util.GetNuGetExePath();

            // Store the dg file for debugging
            var dgPath = Path.Combine(pathContext.WorkingDirectory, "out.dg");
            var envVars = new Dictionary<string, string>()
                {
                    { "NUGET_PERSIST_DG", "true" },
                    { "NUGET_PERSIST_DG_PATH", dgPath }
                };

            string[] args = new string[] {
                    "restore",
                    pathContext.SolutionRoot,
                    "-Verbosity",
                    "detailed"
                };

            // Act
            var r = CommandRunner.Run(
                nugetexe,
                pathContext.WorkingDirectory.Path,
                string.Join(" ", args),
                waitForExit: true,
                environmentVariables: envVars);

            // Assert
            Assert.True(exitCode == r.Item1, r.Item2 + " " + r.Item3);

            return r;
        }
    }
}
