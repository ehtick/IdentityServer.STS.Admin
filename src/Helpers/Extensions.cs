using System;
using System.IO;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;
using IdentityServer.STS.Admin.Configuration;
using IdentityServer.STS.Admin.Resolvers;
using IdentityServer4.Configuration;
using IdentityServer4.EntityFramework.Storage;
using IdentityServer4.Models;
using IdentityServer.STS.Admin.Interfaces;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Pomelo.EntityFrameworkCore.MySql.Storage;

namespace IdentityServer.STS.Admin.Helpers
{
    public static class Extensions
    {
        private const string _authorize = "connect/authorize";
        private const string _authorizeCallback = _authorize + "/callback";


        /// <summary>
        /// 检查重定向地址是否为本地客户端
        /// </summary>
        /// <param name="context"></param>
        /// <returns></returns>
        public static bool IsNativeClient(this AuthorizationRequest context)
        {
            return !context.RedirectUri.StartsWith("https", StringComparison.Ordinal)
                   && !context.RedirectUri.StartsWith("http", StringComparison.Ordinal);
        }

        public static bool IsLocal(this string returnUrl, string currentIp = "")
        {
            if (string.IsNullOrEmpty(returnUrl))
            {
                return false;
            }

            return returnUrl.Contains(currentIp, StringComparison.OrdinalIgnoreCase)
                   || returnUrl.Contains("localhost", StringComparison.OrdinalIgnoreCase)
                   || returnUrl.Contains(_authorize, StringComparison.OrdinalIgnoreCase)
                   || returnUrl.Contains(_authorizeCallback, StringComparison.OrdinalIgnoreCase);
        }

        public static DateTime? ToLocalDateTime(this DateTime? dateTime)
        {
            return dateTime != null
                ? dateTime.Value.Kind == DateTimeKind.Utc
                    ? dateTime.Value.ToLocalTime()
                    : dateTime.Value
                : default;
        }

        public static DateTime? ToLocalDateTime(this DateTimeOffset? dateTime)
        {
            return dateTime != null
                ? dateTime.Value.DateTime.Kind == DateTimeKind.Utc
                    ? dateTime.Value.DateTime.ToLocalTime()
                    : dateTime.Value.DateTime
                : default;
        }


        public static void RegisterDbContexts<TIdentityDbContext, TConfigurationDbContext, TPersistedGrantDbContext, TDataProtectionDbContext>(this IServiceCollection services, IConfiguration configuration)
            where TIdentityDbContext : DbContext
            where TPersistedGrantDbContext : DbContext, IIdsPersistedGrantDbContext
            where TConfigurationDbContext : DbContext, IIdsConfigurationDbContext
            where TDataProtectionDbContext : DbContext, IDataProtectionKeyContext
        {
            var identityConnectionString = configuration.GetConnectionString(ConfigurationConst.IdentityDbConnectionStringKey);
            var configurationConnectionString = configuration.GetConnectionString(ConfigurationConst.ConfigurationDbConnectionStringKey);
            var persistedGrantsConnectionString = configuration.GetConnectionString(ConfigurationConst.PersistedGrantDbConnectionStringKey);
            var dataProtectionConnectionString = configuration.GetConnectionString(ConfigurationConst.DataProtectionDbConnectionStringKey);

            var migrationsAssembly = "IdentityServer.STS.Admin";

            //aspnet core identity 用户操作
            services.AddDbContext<TIdentityDbContext>((provider, options) =>
            {
                options.UseMySql(identityConnectionString, builder => { builder.MigrationsAssembly(migrationsAssembly); });
                var loggerFactory = provider.GetService<ILoggerFactory>();
                options.UseLoggerFactory(loggerFactory);
            });

            //ids 配置操作
            services.AddConfigurationDbContext<TConfigurationDbContext>(options => options.ConfigureDbContext = b => b.UseMySql(configurationConnectionString, sql => sql.MigrationsAssembly(migrationsAssembly)));

            //ids 持久化授权操作
            services.AddOperationalDbContext<TPersistedGrantDbContext>(options => options.ConfigureDbContext = b => b.UseMySql(persistedGrantsConnectionString, sql => sql.MigrationsAssembly(migrationsAssembly)));

            //数据保护
            services.AddDbContext<TDataProtectionDbContext>(options => options.UseMySql(dataProtectionConnectionString, optionsSql => optionsSql.MigrationsAssembly(migrationsAssembly)));
        }

        /// <summary>
        /// Add services for authentication, including Identity model, IdentityServer4 and external providers
        /// </summary>
        /// <typeparam name="TIdentityDbContext">DbContext for Identity</typeparam>
        /// <typeparam name="TUser">User Identity class</typeparam>
        /// <typeparam name="TRole">User Identity Role class</typeparam>
        /// <param name="services"></param>
        /// <param name="configuration"></param>
        public static void AddAspNetIdentityAuthenticationServices<TIdentityDbContext, TUser, TRole>(this IServiceCollection services, IConfiguration configuration)
            where TIdentityDbContext : DbContext
            where TUser : class
            where TRole : class
        {
            services.AddSingleton<IdentityOptions>() //默认配置
                //.AddScoped<ApplicationSignInManager<TUser>>() //用户登录管理器
                .AddIdentity<TUser, TRole>() //用户 角色
                .AddEntityFrameworkStores<TIdentityDbContext>() //aspnetcore user 操作
                .AddDefaultTokenProviders();

            //配置cookie
            services.Configure<CookiePolicyOptions>(options =>
            {
                options.MinimumSameSitePolicy = SameSiteMode.Unspecified;
                options.Secure = CookieSecurePolicy.SameAsRequest;
                options.OnAppendCookie = cookieContext =>
                    AuthenticationHelpers.CheckSameSite(cookieContext.Context, cookieContext.CookieOptions);
                options.OnDeleteCookie = cookieContext =>
                    AuthenticationHelpers.CheckSameSite(cookieContext.Context, cookieContext.CookieOptions);
            });

            services.AddAuthentication()
                .AddGitHub(options =>
                {
                    options.ClientId = "6aced974f4ac1536ff1d";
                    options.ClientSecret = "a9cca44681973f866de814371ee81c70959f651a";
                    options.AccessDeniedPath = "/api/authenticate/externalLoginCallback";

                    options.Scope.Add("user:email");
                    options.Scope.Add("user");
                    options.SaveTokens = true;
                })
                .AddWeibo(options =>
                {
                    options.ClientId = "3217031503";
                    options.ClientSecret = "4b03e98edacf79eaeb75ec131699f52a";
                });

            services.AddAuthorization(options =>
            {
                options.AddPolicy("Admin", policy =>
                {
                    policy.RequireRole("Admin")
                        .RequireAuthenticatedUser();
                });
            });
        }


        public static IIdentityServerBuilder AddIdentityServer<TConfigurationDbContext, TPersistedGrantDbContext, TUserIdentity>(this IServiceCollection services,
            IConfiguration configuration)
            where TPersistedGrantDbContext : DbContext, IIdsPersistedGrantDbContext
            where TConfigurationDbContext : DbContext, IIdsConfigurationDbContext
            where TUserIdentity : class
        {
            var builder = services.AddIdentityServer(options =>
                {
                    options.Events.RaiseErrorEvents = true;
                    options.Events.RaiseInformationEvents = true;
                    options.Events.RaiseFailureEvents = true;
                    options.Events.RaiseSuccessEvents = true;

                    options.UserInteraction.LoginUrl = "http://localhost:8080/signIn";
                    options.UserInteraction.LoginReturnUrlParameter = "returnUrl";

                    options.UserInteraction.ErrorUrl = "http://localhost:8080/error";
                    options.UserInteraction.LogoutUrl = "http://localhost:8080/logout";
                    options.UserInteraction.ConsentUrl = "http://localhost:8080/consent";
                    options.UserInteraction.DeviceVerificationUrl = "http://localhost:8080/device";

                    //options.UserInteraction = new UserInteractionOptions
                    //{
                    //    LoginUrl = "login", //登录地址
                    //    LoginReturnUrlParameter = "returnUrl", //设置登录后的"返回地址"的参数名称默认：returnUrl 
                    //    //LogoutUrl = null, //注销地址            z
                    //    LogoutIdParameter = "logoutId", //注销页面id，默认：logoutId
                    //   // ConsentUrl = null, //授权同意页面
                    //    ConsentReturnUrlParameter = "returnUrl ", //设置"返回地址"的参数名称，默认：returnUrl 
                    //   // ErrorUrl = null, //"错误页面地址"
                    //    ErrorIdParameter = "errorId", //错误id，默认：errorId
                    //    CustomRedirectReturnUrlParameter = "returnUrl", //设置从授权端点传递给自定义重定向的返回URL参数的名称。默认：returnUrl
                    //    CookieMessageThreshold = 5,
                    //    DeviceVerificationUrl = "/device",
                    //    DeviceVerificationUserCodeParameter = "userCode"
                    //};
                })
                .AddConfigurationStore<TConfigurationDbContext>()
                .AddOperationalStore<TPersistedGrantDbContext>()
                .AddAspNetIdentity<TUserIdentity>() //添加aspnetcore user
                .AddCustomSigningCredential(configuration) //证书
                .AddCustomValidationKey(configuration) //密钥
                .AddProfileService<UserProfile>()
                .AddExtensionGrantValidator<DelegationGrantValidator>(); //自定义授权模式

            return builder;
        }

        public static IIdentityServerBuilder AddCustomValidationKey(this IIdentityServerBuilder builder, IConfiguration configuration)
        {
            var certificateConfiguration = configuration.GetSection(nameof(CertificateConfiguration)).Get<CertificateConfiguration>();

            if (certificateConfiguration.UseValidationCertificateThumbprint)
            {
                if (string.IsNullOrWhiteSpace(certificateConfiguration.ValidationCertificateThumbprint))
                {
                    // throw new Exception(ValidationCertificateThumbprintNotFound);
                }

                var certStore = new X509Store(StoreName.My, StoreLocation.LocalMachine);
                certStore.Open(OpenFlags.ReadOnly);

                var certCollection = certStore.Certificates.Find(X509FindType.FindByThumbprint, certificateConfiguration.ValidationCertificateThumbprint, false);
                if (certCollection.Count == 0)
                {
                    //  throw new Exception(CertificateNotFound);
                }

                var certificate = certCollection[0];

                builder.AddValidationKey(certificate);
            }
            else if (certificateConfiguration.UseValidationCertificatePfxFile)
            {
                if (string.IsNullOrWhiteSpace(certificateConfiguration.ValidationCertificatePfxFilePath))
                {
                    //throw new Exception(ValidationCertificatePathIsNotSpecified);
                }

                if (File.Exists(certificateConfiguration.ValidationCertificatePfxFilePath))
                {
                    try
                    {
                        builder.AddValidationKey(new X509Certificate2(certificateConfiguration.ValidationCertificatePfxFilePath, certificateConfiguration.ValidationCertificatePfxFilePassword));
                    }
                    catch (Exception e)
                    {
                        throw new Exception("There was an error adding the key file - during the creation of the validation key", e);
                    }
                }
                else
                {
                    throw new Exception($"Validation key file: {certificateConfiguration.ValidationCertificatePfxFilePath} not found");
                }
            }

            return builder;
        }

        public static IIdentityServerBuilder AddCustomSigningCredential(this IIdentityServerBuilder builder, IConfiguration configuration)
        {
            var certificateConfiguration = configuration.GetSection(nameof(CertificateConfiguration)).Get<CertificateConfiguration>();

            //指纹
            if (certificateConfiguration.UseSigningCertificateThumbprint)
            {
                if (string.IsNullOrWhiteSpace(certificateConfiguration.SigningCertificateThumbprint))
                {
                    //  throw new Exception(SigningCertificateThumbprintNotFound);
                }

                StoreLocation storeLocation;
                var validOnly = certificateConfiguration.CertificateValidOnly;

                // Parse the Certificate StoreLocation
                var certStoreLocationLower = certificateConfiguration.CertificateStoreLocation.ToLower();

                if (certStoreLocationLower == StoreLocation.CurrentUser.ToString().ToLower()
                    || certificateConfiguration.CertificateStoreLocation == ((int) StoreLocation.CurrentUser).ToString())
                {
                    storeLocation = StoreLocation.CurrentUser;
                }
                else if (certStoreLocationLower == StoreLocation.LocalMachine.ToString().ToLower()
                         || certStoreLocationLower == ((int) StoreLocation.LocalMachine).ToString())
                {
                    storeLocation = StoreLocation.LocalMachine;
                }
                else
                {
                    storeLocation = StoreLocation.LocalMachine;
                    validOnly = true;
                }

                // Open Certificate
                var certStore = new X509Store(StoreName.My, storeLocation);
                certStore.Open(OpenFlags.ReadOnly);

                var certCollection = certStore.Certificates.Find(X509FindType.FindByThumbprint, certificateConfiguration.SigningCertificateThumbprint, validOnly);
                if (certCollection.Count == 0)
                {
                    // throw new Exception(CertificateNotFound);
                }

                var certificate = certCollection[0];

                builder.AddSigningCredential(certificate);
            }
            //pfx
            else if (certificateConfiguration.UseSigningCertificatePfxFile)
            {
                if (string.IsNullOrWhiteSpace(certificateConfiguration.SigningCertificatePfxFilePath))
                {
                    //  throw new Exception(SigningCertificatePathIsNotSpecified);
                }

                if (File.Exists(certificateConfiguration.SigningCertificatePfxFilePath))
                {
                    try
                    {
                        builder.AddSigningCredential(new X509Certificate2(certificateConfiguration.SigningCertificatePfxFilePath, certificateConfiguration.SigningCertificatePfxFilePassword));
                    }
                    catch (Exception e)
                    {
                        throw new Exception("创建签名密钥时发生错误", e);
                    }
                }
                else
                {
                    throw new Exception($"签名密钥文件: {certificateConfiguration.SigningCertificatePfxFilePath} not found");
                }
            }
            //开发者签名
            else if (certificateConfiguration.UseTemporarySigningKeyForDevelopment)
            {
                builder.AddDeveloperSigningCredential();
            }
            else
            {
                throw new Exception("签名类型未定义");
            }

            return builder;
        }
    }
}