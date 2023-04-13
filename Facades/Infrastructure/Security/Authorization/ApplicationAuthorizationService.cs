﻿using System.Security;
using System.Security.Claims;
using Havit.Extensions.DependencyInjection.Abstractions;
using Havit.NewProjectTemplate.Facades.Infrastructure.Security.Authentication;
using Havit.NewProjectTemplate.Primitives.Model.Security;
using Havit.NewProjectTemplate.Services.Infrastructure;
using Microsoft.AspNetCore.Authorization;

namespace Havit.NewProjectTemplate.Facades.Infrastructure.Security.Authorization;

[Service(Profile = ServiceProfiles.WebServer)]
public class ApplicationAuthorizationService : IApplicationAuthorizationService
{
	private readonly IApplicationAuthenticationService applicationAuthenticationService;

	public ApplicationAuthorizationService(IApplicationAuthenticationService applicationAuthenticationService)
	{
		this.applicationAuthenticationService = applicationAuthenticationService;
	}

	public IEnumerable<RoleEntry> GetCurrentUserRoles()
	{
		return applicationAuthenticationService.GetCurrentClaimsPrincipal().FindAll(ClaimTypes.Role).Select(c => Enum.Parse<RoleEntry>(c.Value));
	}

	public bool IsCurrentUserInRole(RoleEntry role)
	{
		return applicationAuthenticationService.GetCurrentClaimsPrincipal().IsInRole(role.ToString());
	}
}
