using System.Collections.Generic;
using System.Threading.Tasks;
using IdentityServer.STS.Admin.Constants;
using IdentityServer.STS.Admin.Interfaces.Identity;
using IdentityServer.STS.Admin.Models;
using IdentityServer.STS.Admin.Models.Admin.Identity;
using IdentityServer4.Extensions;
using IdentityServer4.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ApiResource = IdentityServer4.EntityFramework.Entities.ApiResource;
using ApiScope = IdentityServer4.EntityFramework.Entities.ApiScope;
using Client = IdentityServer4.EntityFramework.Entities.Client;
using IdentityResource = IdentityServer4.EntityFramework.Entities.IdentityResource;

namespace IdentityServer.STS.Admin.Controllers.Admin
{
    [Authorize]
    [Route("api/[controller]")]
    [ApiController]
    public class ConfigurationController : ControllerBase
    {
        private readonly IConfigurationService _configurationService;
        private readonly IIdentityResourceService _identityResourceService;
        private readonly IApiResourceService _apiResourceService;
        private readonly IApiScopeService _apiScopeService;
        private readonly IClientService _clientService;

        public ConfigurationController(IConfigurationService configurationService
            , IIdentityResourceService identityResourceService
            , IApiResourceService apiResourceService
            , IApiScopeService apiScopeService
            , IClientService clientService)
        {
            _configurationService = configurationService;
            _identityResourceService = identityResourceService;
            _apiResourceService = apiResourceService;
            _apiScopeService = apiScopeService;
            _clientService = clientService;
        }

        [HttpGet("enums")]
        public Dictionary<string, IEnumerable<SelectItem<int, string>>> GetEnums()
        {
            return new Dictionary<string, IEnumerable<SelectItem<int, string>>>
            {
                ["tokenUsage"] = EnumEx.GetEnumTypes<TokenUsage>(),
                ["accessTokenType"] = EnumEx.GetEnumTypes<AccessTokenType>(),
                ["clientType"] = EnumEx.GetEnumTypes<ClientType>(),
                ["tokenExpiration"] = EnumEx.GetEnumTypes<TokenExpiration>()
            };
        }

        [HttpGet("claims")]
        public IEnumerable<string> GetStandardClaims()
        {
            return _configurationService.GetStandardClaims();
        }


        [HttpGet("grantTypes")]
        public IEnumerable<string> GetGrantTypes()
        {
            return ClientConstants.GrantTypes;
        }


        [HttpGet("scopes")]
        public async Task<IEnumerable<string>> GetScopesAsync()
        {
            return await _clientService.GetScopesAsync();
        }

        #region identity resource

        [HttpGet("identityResource/page")]
        public async Task<Pagination<IdentityResource>> QueryIdentityResourcePage([FromQuery] IdentityResourcePageIn pageIn)
        {
            return await _identityResourceService.QueryIdentityResourcePage(pageIn);
        }

        [HttpGet("identityResource")]
        public async Task<IdentityResource> QueryIdentityResource(int id)
        {
            return await _identityResourceService.QueryIdentityResource(id);
        }

        [HttpPost("identityResource")]
        public async Task SaveIdentityResource(IdentityResource identityResource)
        {
            await _identityResourceService.SaveIdentityResource(identityResource);
        }

        #endregion

        #region api resource

        [HttpGet("apiResource/page")]
        public async Task<Pagination<ApiResource>> QueryApiResourcePage([FromQuery] ApiResourcePageIn pageIn)
        {
            return await _apiResourceService.QueryApiResourcePage(pageIn);
        }


        [HttpGet("apiResource")]
        public async Task<ApiResource> QueryApiResource(int id)
        {
            var res = await _apiResourceService.QueryApiResource(id);
            return res;
        }

        [HttpPost("apiResource")]
        public async Task SaveApiResource(ApiResource apiResource)
        {
            await _apiResourceService.SaveApiResource(apiResource);
        }

        #endregion

        #region api scope

        [HttpGet("apiScope/page")]
        public async Task<Pagination<ApiScope>> QueryApiScopePage([FromQuery] ApiScopePageIn pageIn)
        {
            return await _apiScopeService.QueryApiScopePage(pageIn);
        }


        [HttpGet("apiScope")]
        public async Task<ApiScope> QueryApiScope(int id)
        {
            var res = await _apiScopeService.QueryApiScope(id);
            return res;
        }

        [HttpPost("apiScope")]
        public async Task SaveApiScope(ApiScope apiScope)
        {
            await _apiScopeService.SaveApiScope(apiScope);
        }

        #endregion

        #region client

        [HttpGet("client/page")]
        public async Task<Pagination<Client>> QueryClientPage([FromQuery] ClientSearchPageIn pageIn)
        {
            pageIn.UserId = int.Parse(User.GetSubjectId());
            return await _clientService.QueryClientPage(pageIn);
        }

        [HttpGet("client")]
        public async Task<Client> QueryClientById(int id)
        {
            return await _clientService.QueryClientById(id);
        }

        [HttpPost("client")]
        public async Task SaveClient(ClientInput client)
        {
            var userId = int.Parse(User.Identity.GetSubjectId());
            await _clientService.SaveClient(client, userId);
        }

        [HttpDelete("client/{id:int}")]
        public async Task RemoveClientByIdAsync(int id)
        {
            var userId = int.Parse(User.Identity.GetSubjectId());
            await _clientService.RemoveClientByIdAsync(id, userId);
        }

        [HttpPost("clientSecret")]
        public async Task AddClientSecret(ClientSecretInput input)
        {
            await _clientService.AddSecret(input);
        }

        [HttpDelete("clientSecret")]
        public async Task RemoveClientSecret(int id)
        {
            await _clientService.DeleteSecretAsync(id);
        }

        #endregion
    }
}