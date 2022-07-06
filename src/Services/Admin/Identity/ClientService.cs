﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AutoMapper;
using IdentityServer.STS.Admin.DbContexts;
using IdentityServer.STS.Admin.Entities;
using IdentityServer.STS.Admin.Enums;
using IdentityServer.STS.Admin.Interfaces;
using IdentityServer.STS.Admin.Interfaces.Identity;
using IdentityServer.STS.Admin.Models;
using IdentityServer.STS.Admin.Models.Admin.Identity;
using IdentityServer4.EntityFramework.Entities;
using IdentityServer4.Models;
using Microsoft.EntityFrameworkCore;
using Client = IdentityServer4.EntityFramework.Entities.Client;

namespace IdentityServer.STS.Admin.Services.Admin.Identity
{
    public class ClientService : IClientService
    {
        private readonly IdsConfigurationDbContext _idsConfigurationDbContext;
        private readonly IMapper _mapper;

        private const string SharedSecret = "SharedSecret";

        public ClientService(IdsConfigurationDbContext idsConfigurationDbContext , IMapper mapper)
        {
            _idsConfigurationDbContext = idsConfigurationDbContext;
            _mapper = mapper;
        }


        /// <summary>
        /// hash 处理密码
        /// </summary>
        /// <param name="secret"></param>
        private static void HashClientSharedSecret(ClientSecretInput secret)
        {
            if (secret.Type != SharedSecret) return;

            if (secret.HashType == HashType.Sha256)
                secret.Value = secret.Value.Sha256();
            else if (secret.HashType == HashType.Sha512)
                secret.Value = secret.Value.Sha512();
        }

        public async Task<Pagination<Client>> QueryClientPage(ClientSearchPageIn pageIn)
        {
            var clientIds = await _idsConfigurationDbContext.ClientOwners.AsNoTracking()
                .Where(x => x.UserId == pageIn.UserId)
                .Select(x => x.ClientId)
                .ToListAsync();

            return await _idsConfigurationDbContext.Clients
                .Where(x => clientIds.Contains(x.Id))
                .OrderBy(x => x.Created)
                .ToPagination(pageIn);
        }

        private static string GenerateStringId()
        {
            long i = 1;
            foreach (byte b in Guid.NewGuid().ToByteArray())
            {
                i *= b + 1;
            }

            return $"{i - DateTime.Now.Ticks:x}";
        }


        public async Task SaveClient(ClientInput input, int userId)
        {
            var isAdd = input.Id == 0;
            using (var transaction = await _idsConfigurationDbContext.Database.BeginTransactionAsync())
            {
                try
                {
                    if (isAdd)
                    {
                        var client = PrepareClientTypeForNewClient(input);
                        await _idsConfigurationDbContext.Clients.AddAsync(client);
                        await _idsConfigurationDbContext.SaveChangesAsync();

                        var owner = new ClientOwners
                        {
                            ClientId = client.Id,
                            UserId = userId
                        };
                        await _idsConfigurationDbContext.ClientOwners.AddAsync(owner);
                        await _idsConfigurationDbContext.SaveChangesAsync();
                    }
                    else
                    {
                        var redirectUris = await _idsConfigurationDbContext.ClientRedirectUris.Where(x => x.ClientId == input.Id).ToListAsync();
                        _idsConfigurationDbContext.ClientRedirectUris.RemoveRange(redirectUris);

                        var grantTypes = await _idsConfigurationDbContext.ClientGrantTypes.Where(x => x.ClientId == input.Id).ToListAsync();
                        _idsConfigurationDbContext.ClientGrantTypes.RemoveRange(grantTypes);

                        var postLogoutRedirectUris = await _idsConfigurationDbContext.ClientPostLogoutRedirectUris.Where(x => x.ClientId == input.Id).ToListAsync();
                        _idsConfigurationDbContext.ClientPostLogoutRedirectUris.RemoveRange(postLogoutRedirectUris);

                        var scopes = await _idsConfigurationDbContext.ClientScopes.Where(x => x.ClientId == input.Id).ToListAsync();
                        _idsConfigurationDbContext.ClientScopes.RemoveRange(scopes);

                        var idPRestrictions = await _idsConfigurationDbContext.ClientIdPRestrictions.Where(x => x.ClientId == input.Id).ToListAsync();
                        _idsConfigurationDbContext.ClientIdPRestrictions.RemoveRange(idPRestrictions);

                        var claims = await _idsConfigurationDbContext.ClientClaims.Where(x => x.ClientId == input.Id).ToListAsync();
                        _idsConfigurationDbContext.ClientClaims.RemoveRange(claims);

                        var corsOrigins = await _idsConfigurationDbContext.ClientCorsOrigins.Where(x => x.ClientId == input.Id).ToListAsync();
                        _idsConfigurationDbContext.ClientCorsOrigins.RemoveRange(corsOrigins);

                        _idsConfigurationDbContext.Clients.Update(input);
                        await _idsConfigurationDbContext.SaveChangesAsync();
                    }

                    await transaction.CommitAsync();
                }
                catch (Exception)
                {
                    await transaction.RollbackAsync();
                    throw;
                }
            }
        }

        public async Task RemoveClientByIdAsync(int id, int userId)
        {
            var client = await _idsConfigurationDbContext.Clients.FindAsync(id);
            if (client == null)
            {
                throw new Exception("客户端不存在");
            }

            var owner = await _idsConfigurationDbContext.ClientOwners.FirstOrDefaultAsync(x => x.ClientId == client.Id && x.UserId == userId);
            if (owner == null)
            {
                throw new Exception("客户端所有权有误");
            }

            using (var transaction = await _idsConfigurationDbContext.Database.BeginTransactionAsync())
            {
                try
                {
                    _idsConfigurationDbContext.Clients.Remove(client);
                    _idsConfigurationDbContext.ClientOwners.Remove(owner);

                    await _idsConfigurationDbContext.SaveChangesAsync();
                    await transaction.CommitAsync();
                }
                catch
                {
                    await transaction.RollbackAsync();
                    throw;
                }
            }
        }


        public async Task<IEnumerable<string>> GetScopesAsync()
        {
            var identityResources = await _idsConfigurationDbContext.IdentityResources.AsNoTracking()
                .Select(x => x.Name).ToListAsync();

            var apiScopes = await _idsConfigurationDbContext.ApiScopes.AsNoTracking()
                .Select(x => x.Name).ToListAsync();

            var scopes = identityResources.Concat(apiScopes).Distinct();
            return scopes;
        }


        public async Task<Client> QueryClientById(int id)
        {
            return await _idsConfigurationDbContext.Clients
                .Include(x => x.AllowedGrantTypes)
                .Include(x => x.PostLogoutRedirectUris)
                .Include(x => x.AllowedScopes)
                .Include(x => x.RedirectUris)
                .Include(x => x.IdentityProviderRestrictions)
                .Include(x => x.Claims)
                .Include(x => x.AllowedCorsOrigins)
                .AsSplitQuery()
                .AsNoTracking().SingleOrDefaultAsync(x => x.Id == id);
        }


        public async Task AddSecret(ClientSecretInput clientSecret)
        {
            HashClientSharedSecret(clientSecret);
            var client = await _idsConfigurationDbContext.Clients.Where(x => x.Id == clientSecret.ClientId).SingleOrDefaultAsync();
            clientSecret.Client = client;

            await _idsConfigurationDbContext.ClientSecrets.AddAsync(clientSecret);
            await _idsConfigurationDbContext.SaveChangesAsync();
        }

        public async Task DeleteSecretAsync(int id)
        {
            var clientSecret = await _idsConfigurationDbContext.ClientSecrets.FindAsync(id);
            if (clientSecret == null) return;
            _idsConfigurationDbContext.ClientSecrets.Remove(clientSecret);
            await _idsConfigurationDbContext.SaveChangesAsync();
        }


        #region help methods

        private static IEnumerable<ClientGrantType> TransferClientGrantType(IEnumerable<string> grantTypes)
        {
            foreach (var grantType in grantTypes)
            {
                yield return new ClientGrantType {GrantType = grantType,};
            }
        }


        private  Client PrepareClientTypeForNewClient(ClientInput input)
        {
            var client = _mapper.Map<Client>(input);
            client.ClientId = GenerateStringId();

            switch (input.ClientType)
            {
                case ClientType.Empty:
                    break;
                case ClientType.Web:
                    client.AllowedGrantTypes.AddRange(TransferClientGrantType(GrantTypes.Code));
                    client.RequirePkce = true;
                    client.RequireClientSecret = true;
                    break;
                case ClientType.Spa:
                    client.AllowedGrantTypes.AddRange(TransferClientGrantType(GrantTypes.Code));
                    client.RequirePkce = true;
                    client.RequireClientSecret = false;
                    break;
                case ClientType.Native:
                    client.AllowedGrantTypes.AddRange(TransferClientGrantType(GrantTypes.Code));
                    client.RequirePkce = true;
                    client.RequireClientSecret = false;
                    break;
                case ClientType.Machine:
                    client.AllowedGrantTypes.AddRange(TransferClientGrantType(GrantTypes.ClientCredentials));
                    break;
                case ClientType.Device:
                    client.AllowedGrantTypes.AddRange(TransferClientGrantType(GrantTypes.DeviceFlow));
                    client.RequireClientSecret = false;
                    client.AllowOfflineAccess = true;
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            return client;
        }

        #endregion
    }
}