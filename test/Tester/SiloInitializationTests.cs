using System;
using System.Globalization;
using System.Reflection;
using System.Threading.Tasks;
using Orleans.Runtime;
using Orleans.Runtime.Configuration;
using Orleans.Runtime.Host;
using Orleans.TestingHost;
using Orleans.TestingHost.Utils;
using Xunit;

namespace Tester
{
    public class SiloInitializationTests
    {
        /// <summary>
        /// Tests that a silo host can be successfully started after a prior initialization failure.
        /// </summary>
        [Fact, TestCategory("Functional")]
        public async Task SiloInitializationIsRetryableTest()
        {
            var appDomain = CreateAppDomain();
            appDomain.UnhandledException += (sender, args) =>
            {
                throw new AggregateException("Exception from AppDomain", (Exception) args.ExceptionObject);
            };

            try
            {
                var config = ClusterConfiguration.LocalhostPrimarySilo();
                config.Globals.ClusterId = Guid.NewGuid().ToString();
                var originalLivenessType = config.Globals.LivenessType;
                var originalMembershipAssembly = config.Globals.MembershipTableAssembly;

                // Set a configuration which will cause an early initialization error.
                // Try initializing the cluster, verify that it fails.
                config.Globals.LivenessType = GlobalConfiguration.LivenessProviderType.Custom;
                config.Globals.MembershipTableAssembly = "NonExistentAssembly.jpg";
                
                var siloHost = CreateSiloHost(appDomain, config);
                siloHost.InitializeSilo();

                // Attempt to start the silo.
                await Assert.ThrowsAnyAsync<Exception>(() => siloHost.StartSiloAsync(catchExceptions: false));
                siloHost.UnInitializeSilo();

                // Reset the configuration to a valid configuration.
                config.Globals.LivenessType = originalLivenessType;
                config.Globals.MembershipTableAssembly = originalMembershipAssembly;

                // Starting a new cluster should succeed.
                siloHost = CreateSiloHost(appDomain, config);
                siloHost.InitializeSilo();
                siloHost.StartSilo(catchExceptions: false);
            }
            finally
            {
                AppDomain.Unload(appDomain);
            }
        }

        private static AppDomain CreateAppDomain()
        {
            var currentAppDomain = AppDomain.CurrentDomain;
            var appDomainSetup = new AppDomainSetup
            {
                ApplicationBase = Environment.CurrentDirectory,
                ConfigurationFile = currentAppDomain.SetupInformation.ConfigurationFile,
                ShadowCopyFiles = currentAppDomain.SetupInformation.ShadowCopyFiles,
                ShadowCopyDirectories = currentAppDomain.SetupInformation.ShadowCopyDirectories,
                CachePath = currentAppDomain.SetupInformation.CachePath
            };

            return AppDomain.CreateDomain(nameof(SiloInitializationIsRetryableTest), null, appDomainSetup);
        }

        private static SiloHost CreateSiloHost(AppDomain appDomain, ClusterConfiguration clusterConfig)
        {
            var args = new object[] { nameof(SiloInitializationIsRetryableTest), clusterConfig};

            return (SiloHost)appDomain.CreateInstanceFromAndUnwrap(
                "Orleans.Runtime.Legacy.dll",
                typeof(SiloHost).FullName,
                false,
                BindingFlags.Default,
                null,
                args,
                CultureInfo.CurrentCulture,
                new object[] { });
        }
    }
}