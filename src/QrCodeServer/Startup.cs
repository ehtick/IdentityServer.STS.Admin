using Kurisu.Authentication.Abstractions;
using Kurisu.Authentication.Internal;
using Kurisu.Startup;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace QrCodeServer
{
    public class Startup : DefaultKurisuStartup
    {
        public Startup(IConfiguration configuration) : base(configuration)
        {
        }

        public override void ConfigureServices(IServiceCollection services)
        {
            base.ConfigureServices(services);

            services.AddHttpContextAccessor();
            services.TryAddSingleton<ICurrentUserInfoResolver, DefaultCurrentUserInfoResolver>();
            services.AddMemoryCache();
        }
    }
}