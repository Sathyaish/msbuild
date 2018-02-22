﻿using Microsoft.Build.BackEnd.Logging;
using Microsoft.Build.BackEnd.SdkResolution;
using Microsoft.Build.Construction;
using Microsoft.Build.Framework;
using Microsoft.Build.UnitTests;
using System;
using System.Collections.Generic;
using System.Linq;
using Shouldly;
using Xunit;
using SdkResolverContextBase = Microsoft.Build.Framework.SdkResolverContext;
using SdkResultBase = Microsoft.Build.Framework.SdkResult;
using SdkResultFactoryBase = Microsoft.Build.Framework.SdkResultFactory;
using SdkResultImpl = Microsoft.Build.BackEnd.SdkResolution.SdkResult;

namespace Microsoft.Build.Engine.UnitTests.BackEnd
{
    public class SdkResolverService_Tests
    {
        private readonly MockLogger _logger;
        private readonly LoggingContext _loggingContext;

        public SdkResolverService_Tests()
        {
            _logger = new MockLogger();
            ILoggingService loggingService = LoggingService.CreateLoggingService(LoggerMode.Synchronous, 1);
            loggingService.RegisterLogger(_logger);

            _loggingContext = new MockLoggingContext(
                loggingService,
                new BuildEventContext(0, 0, BuildEventContext.InvalidProjectContextId, 0, 0));
        }

        [Fact]
        public void AssertAllResolverErrorsLoggedWhenSdkNotResolved()
        {
            SdkResolverService.Instance.InitializeForTests(new MockLoaderStrategy());

            SdkReference sdk = new SdkReference("notfound", "referencedVersion", "minimumVersion");

            string result = SdkResolverService.Instance.ResolveSdk(BuildEventContext.InvalidSubmissionId, sdk, _loggingContext, new MockElementLocation("file"), "sln", "projectPath");

            result.ShouldBeNull();

            _logger.BuildMessageEvents.Select(i => i.Message).ShouldContain("MockSdkResolver1 running");
            _logger.BuildMessageEvents.Select(i => i.Message).ShouldContain("MockSdkResolver2 running");
            _logger.Errors.Select(i => i.Message).ShouldBe(new [] { "ERROR1", "ERROR2" });
            _logger.Warnings.Select(i => i.Message).ShouldBe(new[] { "WARNING2" });
        }

        [Fact]
        public void AssertResolutionWarnsIfResolvedVersionIsDifferentFromReferencedVersion()
        {
            var sdk = new SdkReference("foo", "1.0.0", null);

            SdkResolverService.Instance.InitializeForTests(
                null,
                new List<SdkResolver>
                {
                    new ConfigurableMockResolver(
                        new SdkResultImpl(
                            sdk,
                            "path",
                            "2.0.0",
                            Enumerable.Empty<string>()
                            ))
                });

            string result = SdkResolverService.Instance.ResolveSdk(BuildEventContext.InvalidSubmissionId, sdk, _loggingContext, new MockElementLocation("file"), "sln", "projectPath");

            result.ShouldBe("path");

            _logger.WarningCount.ShouldBe(1);
            _logger.Warnings.First().Code.ShouldStartWith("MSB4241");
        }

        [Fact]
        public void AssertErrorLoggedWhenResolverThrows()
        {
            SdkResolverService.Instance.InitializeForTests(new MockLoaderStrategy(includeErrorResolver: true));

            SdkReference sdk = new SdkReference("1sdkName", "version1", "minimumVersion");

            string result = SdkResolverService.Instance.ResolveSdk(BuildEventContext.InvalidSubmissionId, sdk, _loggingContext, new MockElementLocation("file"), "sln", "projectPath");

            result.ShouldBe("resolverpath1");
            _logger.Warnings.Select(i => i.Message).ShouldBe(new [] { "The SDK resolver \"MockSdkResolverThrows\" failed to run. EXMESSAGE" });
        }

        [Fact]
        public void AssertFirstResolverCanResolve()
        {
            SdkResolverService.Instance.InitializeForTests(new MockLoaderStrategy());

            SdkReference sdk = new SdkReference("1sdkName", "referencedVersion", "minimumVersion");

            string result = SdkResolverService.Instance.ResolveSdk(BuildEventContext.InvalidSubmissionId, sdk, _loggingContext, new MockElementLocation("file"), "sln", "projectPath");

            Assert.Equal("resolverpath1", result);
            _logger.BuildMessageEvents.Select(i => i.Message).ShouldContain("MockSdkResolver1 running");
        }

        [Fact]
        public void AssertFirstResolverErrorsSupressedWhenResolved()
        {
            SdkResolverService.Instance.InitializeForTests(new MockLoaderStrategy());

            // 2sdkName will cause MockSdkResolver1 to fail with an error reason. The error will not
            // be logged because MockSdkResolver2 will succeed.
            SdkReference sdk = new SdkReference("2sdkName", "version2", "minimumVersion");

            string result = SdkResolverService.Instance.ResolveSdk(BuildEventContext.InvalidSubmissionId, sdk, _loggingContext, new MockElementLocation("file"), "sln", "projectPath");

            result.ShouldBe("resolverpath2");

            // Both resolvers should run, and no ERROR string.
            _logger.BuildMessageEvents.Select(i => i.Message).ShouldContain("MockSdkResolver1 running");
            _logger.BuildMessageEvents.Select(i => i.Message).ShouldContain("MockSdkResolver2 running");

            // Resolver2 gives a warning on success or failure.
            _logger.Warnings.Select(i => i.Message).ShouldBe(new[] { "WARNING2" });
            _logger.ErrorCount.ShouldBe(0);
        }

        [Fact]
        public void AssertResolverHasStatePreserved()
        {
            const int submissionId = 5;

            SdkResolverService.Instance.InitializeForTests(new MockLoaderStrategy());

            SdkReference sdk = new SdkReference("othersdk", "1.0", "minimumVersion");

            // First call should not know state
            SdkResolverService.Instance.ResolveSdk(submissionId, sdk, _loggingContext, new MockElementLocation("file"), "sln", "projectPath").ShouldBe("resolverpath");

            // Second call should have received state
            SdkResolverService.Instance.ResolveSdk(submissionId, sdk, _loggingContext, new MockElementLocation("file"), "sln", "projectPath").ShouldBe(MockSdkResolverWithState.Expected);
        }

        [Fact]
        public void AssertResolverStateNotPreserved()
        {
            const int submissionId = BuildEventContext.InvalidSubmissionId;

            SdkResolverService.Instance.InitializeForTests(new MockLoaderStrategy());

            SdkReference sdk = new SdkReference("othersdk", "1.0", "minimumVersion");

            // First call should not know state
            SdkResolverService.Instance.ResolveSdk(submissionId, sdk, _loggingContext, new MockElementLocation("file"), "sln", "projectPath").ShouldBe("resolverpath");

            // Second call should have received state
            SdkResolverService.Instance.ResolveSdk(submissionId, sdk, _loggingContext, new MockElementLocation("file"), "sln", "projectPath").ShouldBe("resolverpath");
        }

        [Theory]
        [InlineData(null, "1.0", true)]
        [InlineData("1.0", "1.0", true)]
        [InlineData("1.0-preview", "1.0-PrEvIeW", true)]
        [InlineData("1.0", "1.0.0", false)]
        [InlineData("1.0", "1.0.0.0", false)]
        [InlineData("1.0.0", "1.0.0.0", false)]
        [InlineData("1.2.0.0", "1.0.0.0", false)]
        [InlineData("1.2.3.0", "1.2.0.0", false)]
        [InlineData("1.2.3.4", "1.2.3.0", false)]
        public void IsReferenceSameVersionTests(string version1, string version2, bool expected)
        {
            SdkReference sdk = new SdkReference("Microsoft.NET.Sdk", version1, null);

            SdkResolverService.IsReferenceSameVersion(sdk, version2).ShouldBe(expected);
        }

        private class MockLoaderStrategy : SdkResolverLoader
        {
            private readonly bool _includeErrorResolver;

            public MockLoaderStrategy(bool includeErrorResolver = false)
            {
                _includeErrorResolver = includeErrorResolver;
            }

            internal override IList<SdkResolver> LoadResolvers(LoggingContext loggingContext, ElementLocation location)
            {
                List<SdkResolver> resolvers = new List<SdkResolver>
                {
                    new MockSdkResolver1(),
                    new MockSdkResolver2(),
                    new MockResolverReturnsNull(),
                    new MockSdkResolverWithState()
                };

                if (_includeErrorResolver)
                {
                    resolvers.Add(new MockSdkResolverThrows());
                }

                return resolvers.OrderBy(i => i.Priority).ToList();
            }
        }

        private class MockResolverReturnsNull : SdkResolver
        {
            public override string Name => nameof(MockResolverReturnsNull);

            public override int Priority => -1;

            public override SdkResultBase Resolve(SdkReference sdkReference, SdkResolverContextBase resolverContext, SdkResultFactoryBase factory) => null;
        }

        private class ConfigurableMockResolver : SdkResolver
        {
            private readonly SdkResultImpl _result;

            public ConfigurableMockResolver(SdkResultImpl result)
            {
                _result = result;
            }

            public override string Name => nameof(ConfigurableMockResolver);

            public override int Priority => int.MaxValue;

            public override SdkResultBase Resolve(SdkReference sdkReference, SdkResolverContextBase resolverContext, SdkResultFactoryBase factory) => _result;
        }

        private class MockSdkResolver1 : SdkResolver
        {
            public override string Name => nameof(MockSdkResolver1);

            public override int Priority => 1;

            public override SdkResultBase Resolve(SdkReference sdk, SdkResolverContextBase resolverContext, SdkResultFactoryBase factory)
            {
                resolverContext.Logger.LogMessage("MockSdkResolver1 running", MessageImportance.Normal);

                if (sdk.Name.StartsWith("1"))
                    return factory.IndicateSuccess("resolverpath1", "version1");

                return factory.IndicateFailure(new[] {"ERROR1"});
            }
        }

        private class MockSdkResolver2 : SdkResolver
        {
            public override string Name => nameof(MockSdkResolver2);

            public override int Priority => 2;

            public override SdkResultBase Resolve(SdkReference sdk, SdkResolverContextBase resolverContext, SdkResultFactoryBase factory)
            {
                resolverContext.Logger.LogMessage("MockSdkResolver2 running", MessageImportance.Normal);

                if (sdk.Name.StartsWith("2"))
                    return factory.IndicateSuccess("resolverpath2", "version2", new[] {"WARNING2"});

                return factory.IndicateFailure(new[] {"ERROR2"}, new[] {"WARNING2"});
            }
        }

        private class MockSdkResolverWithState : SdkResolver
        {
            public const string Expected = "01713226A202458F97D9074168DF2618";

            public override string Name => nameof(MockSdkResolverWithState);

            public override int Priority => 3;

            public override SdkResultBase Resolve(SdkReference sdkReference, SdkResolverContextBase resolverContext, SdkResultFactoryBase factory)
            {
                if (sdkReference.Name.Equals("notfound"))
                {
                    return null;
                }
                if (resolverContext.State != null)
                {
                    return factory.IndicateSuccess((string) resolverContext.State, "1.0");
                }

                resolverContext.State = Expected;

                return factory.IndicateSuccess("resolverpath", "1.0");
            }
        }

        private class MockSdkResolverThrows : SdkResolver
        {
            public override string Name => nameof(MockSdkResolverThrows);
            public override int Priority => 0;

            public override SdkResultBase Resolve(SdkReference sdk, SdkResolverContextBase resolverContext, SdkResultFactoryBase factory)
            {
                resolverContext.Logger.LogMessage("MockSdkResolverThrows running", MessageImportance.Normal);

                throw new ArithmeticException("EXMESSAGE");
            }
        }
    }
}
