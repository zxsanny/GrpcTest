﻿using Hangfire;
using Hangfire.Dashboard;
using Havit.Blazor.Grpc.Server;
using Havit.NewProjectTemplate.Contracts;
using Havit.NewProjectTemplate.Contracts.Infrastructure;
using Havit.NewProjectTemplate.DependencyInjection;
using Havit.NewProjectTemplate.Facades.Infrastructure.Security;
using Havit.NewProjectTemplate.Model.Security;
using Havit.NewProjectTemplate.Primitives.Model.Security;
using Havit.NewProjectTemplate.Services.HealthChecks;
using Havit.NewProjectTemplate.Services.Infrastructure.MigrationTool;
using Havit.NewProjectTemplate.Web.Server.Infrastructure.ApplicationInsights;
using Havit.NewProjectTemplate.Web.Server.Infrastructure.ConfigurationExtensions;
using Havit.NewProjectTemplate.Web.Server.Infrastructure.HealthChecks;
using Microsoft.ApplicationInsights.DependencyCollector;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.DependencyInjection;
using ProtoBuf.Grpc.Server;

namespace Havit.NewProjectTemplate.Web.Server;

public class Startup
{
	private readonly IConfiguration configuration;

	public Startup(IConfiguration configuration)
	{
		this.configuration = configuration;
	}

	public void ConfigureServices(IServiceCollection services)
	{
		services.ConfigureForWebServer(configuration);

		services.AddSingleton<IHttpContextAccessor, HttpContextAccessor>();

		services.AddDatabaseDeveloperPageExceptionFilter();

		services.AddOptions();

		services.AddCustomizedMailing(configuration);

		// SmtpExceptionMonitoring to errors@havit.cz
		services.AddExceptionMonitoring(configuration);

		// Application Insights
		services.AddApplicationInsightsTelemetry(configuration);
		services.AddSingleton<ITelemetryInitializer, GrpcRequestStatusTelemetryInitializer>();
		services.AddSingleton<ITelemetryInitializer, EnrichmentTelemetryInitializer>();
		services.ConfigureTelemetryModule<DependencyTrackingTelemetryModule>((module, o) => { module.EnableSqlCommandTextInstrumentation = true; });

		services.AddAuthorization(options =>
		{
			options.AddPolicy(PolicyNames.HangfireDashboardAcccessPolicy, policy => policy
					.AddAuthenticationSchemes(OpenIdConnectDefaults.AuthenticationScheme)
					.RequireAuthenticatedUser()
					.RequireRole(nameof(RoleEntry.SystemAdministrator)));
		});
		services.AddCustomizedAuth(configuration);

		// server-side UI
		services.AddControllersWithViews();
		services.AddRazorPages();

		// gRPC
		services.AddGrpcServerInfrastructure(assemblyToScanForDataContracts: typeof(Dto).Assembly);
		services.AddCodeFirstGrpcReflection();

		// Health checks
		TimeSpan defaultHealthCheckTimeout = TimeSpan.FromSeconds(10);
		services.AddHealthChecks()
			.AddCheck<NewProjectTemplateDbContextHealthCheck>("Database", timeout: defaultHealthCheckTimeout)
			.AddCheck<MailServiceHealthCheck>("SMTP", timeout: defaultHealthCheckTimeout);

		// Hangfire
		services.AddCustomizedHangfire(configuration);
	}

	// This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
	public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
	{
		if (env.IsDevelopment())
		{
			app.UseDeveloperExceptionPage();
			app.UseMigrationsEndPoint();
			app.UseWebAssemblyDebugging();
		}
		else
		{
			app.UseExceptionHandler("/error");
			// The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
			// TODO app.UseHsts();
		}

		app.UseHttpsRedirection();
		app.UseBlazorFrameworkFiles();
		app.UseStaticFiles();

		app.UseExceptionMonitoring();

		app.UseRouting();

		app.UseAuthentication();
		app.UseAuthorization();

		app.UseGrpcWeb(new GrpcWebOptions() { DefaultEnabled = true });

		app.UseEndpoints(endpoints =>
		{
			endpoints.MapRazorPages();
			endpoints.MapControllers();
			endpoints.MapFallbackToPage("/_Host");

			endpoints.MapGrpcServicesByApiContractAttributes(
				typeof(IDataSeedFacade).Assembly,
				configureEndpointWithAuthorization: endpoint =>
				{
					endpoint.RequireAuthorization(); // TODO? AuthorizationPolicyNames.ApiScopePolicy when needed
				});
			endpoints.MapCodeFirstGrpcReflectionService();

			endpoints.MapHealthChecks("/health", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
			{
				AllowCachingResponses = false,
				ResponseWriter = HealthCheckWriter.WriteResponse
			});

			endpoints.MapHangfireDashboard("/hangfire", new DashboardOptions
			{
				Authorization = new List<IDashboardAuthorizationFilter>() { }, // see https://sahansera.dev/securing-hangfire-dashboard-with-endpoint-routing-auth-policy-aspnetcore/
				DisplayStorageConnectionString = false,
				DashboardTitle = "NewProjectTemplate - Jobs",
				StatsPollingInterval = 60_000, // once a minute
				DisplayNameFunc = (_, job) => Havit.Hangfire.Extensions.Helpers.JobNameHelper.TryGetSimpleName(job, out string simpleName)
													? simpleName
													: job.ToString()
			})
			//.RequireAuthorization();
			.RequireAuthorization(PolicyNames.HangfireDashboardAcccessPolicy);
		});

		if (configuration.GetValue<bool>("AppSettings:Migrations:RunMigrations"))
		{
			app.ApplicationServices.GetRequiredService<IMigrationService>().UpgradeDatabaseSchemaAndData();
		}
	}
}
