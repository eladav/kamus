﻿using System;
using System.Threading.Tasks;
using Hamuste.KubernetesAuthentication;
using k8s;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.KeyVault;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Clients.ActiveDirectory;
using Serilog;
using Serilog.Formatting.Json;
using System.Linq;

namespace Hamuste
{
    public class Startup {
        public Startup () { }

        public IConfiguration Configuration;

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices (IServiceCollection services) {

            services.AddMvc (options => options.AddMetricsResourceFilter ());

            services.AddSwaggerGen (swagger => {
                swagger.SwaggerDoc ("v1", new Swashbuckle.AspNetCore.Swagger.Info { Title = "CoreWebApi Swagger" });
            });

            services.AddSingleton<IKubernetes>(s =>
            {
                Console.WriteLine("Adding Kubernetes");
                try
                {
                    KubernetesClientConfiguration config;
                    if (!string.IsNullOrEmpty(Configuration["Kubernetes:ProxyUrl"]))
                    {
                        config = new KubernetesClientConfiguration { Host = Configuration["Kubernetes:ProxyUrl"] };
                    }
                    else
                    {
                        config = KubernetesClientConfiguration.InClusterConfig();
                    }

                    return new Kubernetes(config);
                }catch (Exception e){
                    Console.WriteLine($"Oh no! {e}");
                    throw e;
                }
            });

            services.AddSingleton<IKeyVaultClient>(s =>
            {
                return new KeyVaultClient(GetToken);
            });

            services.AddScheme<KubernetesAuthenticationOptions, KubernetesAuthenticationHandler>("kubernetes", null);

            services.AddAuthorization(options => {
                options.AddPolicy("KubernetesPolicy", policyBuilder => policyBuilder.RequireAssertion(
                    context => context.Resource as string == context.User.Claims.FirstOrDefault(claim => claim.Type == "sub").Value)
               );
            });
        }

        public async Task<string> GetToken(string authority, string resource, string scope)
        {
            Console.WriteLine("Requesting a token!");
            var clientId = Configuration["ActiveDirectory:ClientId"];
            var clientSecret = Configuration["ActiveDirectory:ClientSecret"];
            
            var authContext = new AuthenticationContext(authority);
            ClientCredential clientCred = new ClientCredential(clientId, clientSecret);
            AuthenticationResult result = await authContext.AcquireTokenAsync(resource, clientCred);

            if (result == null)
                throw new InvalidOperationException("Failed to obtain the JWT token");

            return result.AccessToken;
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure (IApplicationBuilder app, IHostingEnvironment env) {
            string appsettingsPath = "appsettings.json";

            Console.WriteLine("Look at me, I'm a beautiful creature!");

            if (env.IsDevelopment ()) {
                app.UseDeveloperExceptionPage ();
                appsettingsPath = "appsettings.Development.json";
            }
            else {
                app.UseExceptionMiddleware();
            }

            var builder = new ConfigurationBuilder ()
                .SetBasePath (env.ContentRootPath)
                .AddJsonFile (appsettingsPath, optional : true, reloadOnChange : true)
                .AddJsonFile ("secrets/appsettings.secrets.json", optional : true)
                .AddEnvironmentVariables ();

            Configuration = builder.Build ();

            Log.Logger = new LoggerConfiguration ()
                .ReadFrom.Configuration (Configuration)
                .CreateLogger ();


            app.UseSwagger ();
            app.UseSwaggerUI (c => {
                c.SwaggerEndpoint ("/swagger/v1/swagger.json", "My First Swagger");
            });

            app.UseAuthentication();

            app.UseMvc ();
        }
    }
}