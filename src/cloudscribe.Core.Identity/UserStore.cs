﻿// Copyright (c) Source Tree Solutions, LLC. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.
// Author:					Joe Audette
// Created:				    2014-07-22
// Last Modified:		    2016-05-10
// 

using cloudscribe.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.OptionsModel;
using Microsoft.AspNet.Identity;
using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;


namespace cloudscribe.Core.Identity
{
    public sealed class UserStore<TUser> :
        IUserStore<TUser>,
        IUserSecurityStampStore<TUser>,
        IUserPasswordStore<TUser>,
        IUserEmailStore<TUser>,
        IUserLoginStore<TUser>,
        IUserRoleStore<TUser>,
        IUserClaimStore<TUser>,
        IUserPhoneNumberStore<TUser>,
        IUserLockoutStore<TUser>,
        IUserTwoFactorStore<TUser>
        where TUser : SiteUser
    {
        
        private UserStore() { }

        public UserStore(
            SiteSettings currentSite,
            ILogger<UserStore<TUser>> logger,
            IUserCommands userCommands,
            IUserQueries userQueries,
            IOptions<MultiTenantOptions> multiTenantOptionsAccessor
            )
        {
            
            log = logger;

            if (currentSite == null) { throw new ArgumentNullException(nameof(currentSite)); }

            _siteSettings = currentSite;

            if (userCommands == null) { throw new ArgumentNullException(nameof(userCommands)); }
            commands = userCommands;

            if (userQueries == null) { throw new ArgumentNullException(nameof(userQueries)); }
            queries = userQueries;


            multiTenantOptions = multiTenantOptionsAccessor.Value;

            //debugLog = config.GetOrDefault("AppSettings:UserStoreDebugEnabled", false);

            if (debugLog) { log.LogInformation("constructor"); }
        }

        private ILogger log;
        private bool debugLog = false;
        private ISiteSettings _siteSettings = null;
        private MultiTenantOptions multiTenantOptions;
        
        private ISiteSettings siteSettings
        {
            get { return _siteSettings; }
        }
  
        private IUserCommands commands;
        private IUserQueries queries;

        #region IUserStore


        public async Task<IdentityResult> CreateAsync(TUser user, CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            cancellationToken.ThrowIfCancellationRequested();
            if (debugLog) { log.LogInformation("CreateAsync"); }
            
            if (user == null)
            {
                throw new ArgumentNullException("user");
            }

            if (user.SiteId == Guid.Empty)
            {
                user.SiteId = siteSettings.Id;
            }
            

            if (user.UserName.Length == 0)
            {
                user.UserName = await SuggestLoginNameFromEmail(siteSettings.Id, user.Email);
            }

            if (user.DisplayName.Length == 0)
            {
                user.DisplayName = await SuggestLoginNameFromEmail(siteSettings.Id, user.Email);
            }

            cancellationToken.ThrowIfCancellationRequested();

            await commands.Create(user, cancellationToken);
            await AddUserToDefaultRoles(user, cancellationToken);
              
            return IdentityResult.Success;


        }

        private async Task AddUserToDefaultRoles(ISiteUser siteUser, CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            cancellationToken.ThrowIfCancellationRequested();
            ISiteRole role;
            
            string defaultRoles = multiTenantOptions.DefaultNewUserRoles;

            if (defaultRoles.Length > 0)
            {
                if (defaultRoles.IndexOf(";") == -1)
                {
                    role = await queries.FetchRole(siteUser.SiteId, defaultRoles, cancellationToken);
                    if ((role != null) && (role.Id != Guid.Empty))
                    {
                        await commands.AddUserToRole(role.Id, siteUser.Id, cancellationToken);
                    }
                }
                else
                {
                    string[] roleArray = defaultRoles.Split(';');
                    foreach (string roleName in roleArray)
                    {
                        if (!string.IsNullOrWhiteSpace(roleName))
                        {
                            role = await queries.FetchRole(siteUser.SiteId, roleName, cancellationToken);
                            if ((role != null) && (role.Id != Guid.Empty))
                            {
                                await commands.AddUserToRole(role.Id, siteUser.Id, cancellationToken);
                            }
                        }
                    }

                }

            }

           
        }

        public async Task<IdentityResult> UpdateAsync(TUser user, CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            cancellationToken.ThrowIfCancellationRequested();
            if (debugLog) { log.LogInformation("UpdateAsync"); }
            
            if (user == null)
            {
                throw new ArgumentNullException("user");
            }
            await commands.Update(user, cancellationToken);

            return IdentityResult.Success;

        }

        public async Task<IdentityResult> DeleteAsync(TUser user, CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            cancellationToken.ThrowIfCancellationRequested();
            if (debugLog) { log.LogInformation("UpdateAsync"); }

            if (siteSettings.ReallyDeleteUsers)
            {
                //Task[] tasks = new Task[4];
                //cancellationToken.ThrowIfCancellationRequested();
                //tasks[0] = repo.DeleteLoginsByUser(-1, user.Id);
                //cancellationToken.ThrowIfCancellationRequested();
                //tasks[1] = repo.DeleteClaimsByUser(-1, user.Id);
                //cancellationToken.ThrowIfCancellationRequested();
                //tasks[2] = repo.DeleteUserRoles(user.UserId);
                //cancellationToken.ThrowIfCancellationRequested();
                //tasks[3] = repo.Delete(user.UserId);
                //cancellationToken.ThrowIfCancellationRequested();

                //Task.WaitAll(tasks);
                await commands.Delete(user.SiteId, user.Id, cancellationToken);
            }
            else
            {
                await commands.FlagAsDeleted(user.Id, cancellationToken);
            }

            return IdentityResult.Success;
        }

        public async Task<TUser> FindByIdAsync(string userId, CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            cancellationToken.ThrowIfCancellationRequested();
            if (debugLog) { log.LogInformation("FindByIdAsync"); }
            
            var userGuid = new Guid(userId);
            var siteGuid = siteSettings.Id;
            if (multiTenantOptions.UseRelatedSitesMode) { siteGuid = multiTenantOptions.RelatedSiteGuid; }

            var siteUser = await queries.Fetch(siteGuid, userGuid, cancellationToken);

            return (TUser)siteUser;
        }


        public async Task<TUser> FindByNameAsync(string normailzedUserName, CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            cancellationToken.ThrowIfCancellationRequested();
            if (debugLog) { log.LogInformation("FindByNameAsync"); }
            
            var siteGuid = siteSettings.Id;
            if (multiTenantOptions.UseRelatedSitesMode) { siteGuid = multiTenantOptions.RelatedSiteGuid; }

            var siteUser = await queries.FetchByLoginName(siteGuid, normailzedUserName, true, cancellationToken);
            return (TUser)siteUser;

        }

        public Task SetUserNameAsync(TUser user, string userName, CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            cancellationToken.ThrowIfCancellationRequested();
            if (debugLog) { log.LogInformation("SetEmailAsync"); }
            
            if (user == null)
            {
                throw new ArgumentNullException("user");
            }

            if (user.SiteId == Guid.Empty)
            {
                user.SiteId = siteSettings.Id;
            }
            
            user.UserName = userName;

            //bool result = await repo.Save(user, cancellationToken);

            return Task.FromResult(0);
        }

        public Task SetNormalizedUserNameAsync(TUser user, string normalizedUserName, CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            cancellationToken.ThrowIfCancellationRequested();
            if (debugLog) { log.LogInformation("SetNormalizedUserNameAsync"); }
            
            if (user == null)
            {
                throw new ArgumentNullException("user");
            }

            if (user.SiteId == Guid.Empty)
            {
                user.SiteId = siteSettings.Id;
            }
            
            user.NormalizedUserName = normalizedUserName;
            //cancellationToken.ThrowIfCancellationRequested();
            //bool result = await repo.Save(user);

            return Task.FromResult(0);

        }

        
//#pragma warning disable 1998

        public Task<string> GetUserIdAsync(TUser user, CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            cancellationToken.ThrowIfCancellationRequested();
            if (debugLog) { log.LogInformation("GetUserIdAsync"); }
            
            if (user == null)
            {
                throw new ArgumentNullException("user");
            }
            return Task.FromResult(user.Id.ToString());

            
        }

        public Task<string> GetUserNameAsync(TUser user, CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            cancellationToken.ThrowIfCancellationRequested();
            if (debugLog) { log.LogInformation("GetUserNameAsync"); }
            
            if (user == null)
            {
                throw new ArgumentNullException("user");
            }
            return Task.FromResult(user.UserName);
        }

        public Task<string> GetNormalizedUserNameAsync(TUser user, CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            cancellationToken.ThrowIfCancellationRequested();
            if (debugLog) { log.LogInformation("GetUserNameAsync"); }
            
            if (user == null)
            {
                throw new ArgumentNullException("user");
            }
            return Task.FromResult(user.NormalizedUserName);

            //return user.UserName.ToLowerInvariant();
        }

        //#pragma warning restore 1998

        #endregion

        #region ISecurityStampStore

        /// <summary>
        /// Sets the provided security <paramref name="stamp"/> for the specified <paramref name="user"/>.
        /// </summary>
        /// <param name="user">The user whose security stamp should be set.</param>
        /// <param name="stamp">The security stamp to set.</param>
        /// <param name="cancellationToken">The <see cref="CancellationToken"/> used to propagate notifications that the operation should be canceled.</param>
        /// <returns>The <see cref="Task"/> that represents the asynchronous operation.</returns>
        public Task SetSecurityStampAsync(TUser user, string stamp, CancellationToken cancellationToken = default(CancellationToken))
        {
            ThrowIfDisposed();
            cancellationToken.ThrowIfCancellationRequested();
            if (user == null)
            {
                throw new ArgumentNullException(nameof(user));
            }
            user.SecurityStamp = stamp;
            return Task.FromResult(0);
            //bool result = await repo.Save(user, cancellationToken);

        }

        /// <summary>
        /// Get the security stamp for the specified <paramref name="user" />.
        /// </summary>
        /// <param name="user">The user whose security stamp should be set.</param>
        /// <param name="cancellationToken">The <see cref="CancellationToken"/> used to propagate notifications that the operation should be canceled.</param>
        /// <returns>The <see cref="Task"/> that represents the asynchronous operation, containing the security stamp for the specified <paramref name="user"/>.</returns>
        public Task<string> GetSecurityStampAsync(TUser user, CancellationToken cancellationToken = default(CancellationToken))
        {
            ThrowIfDisposed();
            cancellationToken.ThrowIfCancellationRequested();
            if (user == null)
            {
                throw new ArgumentNullException(nameof(user));
            }
            return Task.FromResult(user.SecurityStamp);
        }

        #endregion


        #region IUserEmailStore



        public Task<string> GetEmailAsync(TUser user, CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            cancellationToken.ThrowIfCancellationRequested();
            if (debugLog) { log.LogInformation("GetEmailAsync"); }
            
            if (user == null)
            {
                throw new ArgumentNullException("user");
            }

            return Task.FromResult(user.Email);
        }

        public Task<string> GetNormalizedEmailAsync(TUser user, CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            cancellationToken.ThrowIfCancellationRequested();
            if (debugLog) { log.LogInformation("GetEmailAsync"); }
            
            if (user == null)
            {
                throw new ArgumentNullException("user");
            }

            return Task.FromResult(user.NormalizedEmail);
        }



        public Task<bool> GetEmailConfirmedAsync(TUser user, CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            cancellationToken.ThrowIfCancellationRequested();
            if (debugLog) { log.LogInformation("GetEmailConfirmedAsync"); }
            
            if (user == null)
            {
                throw new ArgumentNullException("user");
            }

            return Task.FromResult(user.EmailConfirmed);
        }



        public Task SetEmailAsync(TUser user, string email, CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            cancellationToken.ThrowIfCancellationRequested();
            if (debugLog) { log.LogInformation("SetEmailAsync"); }
            
            if (user == null)
            {
                throw new ArgumentNullException("user");
            }

            if (user.SiteId == Guid.Empty)
            {
                user.SiteId = siteSettings.Id;
            }
            
            cancellationToken.ThrowIfCancellationRequested();

            user.Email = email;
            //user.NormalizedEmail = email.ToLower();
            // TODO: are we supposed to save here?
            //bool result = await repo.Save(user, cancellationToken);
            return Task.FromResult(0);
        }

        public Task SetNormalizedEmailAsync(TUser user, string email, CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            cancellationToken.ThrowIfCancellationRequested();
            if (debugLog) { log.LogInformation("SetEmailAsync"); }
            
            if (user == null)
            {
                throw new ArgumentNullException("user");
            }

            if (user.SiteId == Guid.Empty)
            {
                user.SiteId = siteSettings.Id;
            }
            
            //user.Email = email;
            user.NormalizedEmail = email;

            //cancellationToken.ThrowIfCancellationRequested();

            //bool result = await repo.Save(user);
            return Task.FromResult(0);

        }

        public Task SetEmailConfirmedAsync(TUser user, bool confirmed, CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            cancellationToken.ThrowIfCancellationRequested();
            if (debugLog) { log.LogInformation("SetEmailConfirmedAsync"); }
            
            if (user == null)
            {
                throw new ArgumentNullException("user");
            }

            if (user.SiteId == Guid.Empty)
            {
                user.SiteId = siteSettings.Id;
            }
            
            user.EmailConfirmed = confirmed;

           
            // I don't not sure this method is expected to
            // persist this change to the db
            //bool result = await repo.Save(user);
            return Task.FromResult(0);

        }

        public async Task<TUser> FindByEmailAsync(string email, CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            cancellationToken.ThrowIfCancellationRequested();
            if (debugLog) { log.LogInformation("FindByEmailAsync"); }
            
            Guid siteGuid = siteSettings.Id;
            if (multiTenantOptions.UseRelatedSitesMode) { siteGuid = multiTenantOptions.RelatedSiteGuid; }

            ISiteUser siteUser = await queries.Fetch(siteGuid, email, cancellationToken);

            return (TUser)siteUser;
        }

        #endregion

        #region IUserPasswordStore

        
//#pragma warning disable 1998

        public Task<string> GetPasswordHashAsync(TUser user, CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            cancellationToken.ThrowIfCancellationRequested();
            if (debugLog) { log.LogInformation("GetPasswordHashAsync"); }
            
            if (user == null)
            {
                throw new ArgumentNullException("user");
            }
            
            return Task.FromResult(user.PasswordHash);
        }

        public Task<bool> HasPasswordAsync(TUser user, CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            cancellationToken.ThrowIfCancellationRequested();
            if (debugLog) { log.LogInformation("HasPasswordAsync"); }
            
            if (user == null)
            {
                throw new ArgumentNullException("user");
            }

            return Task.FromResult(string.IsNullOrEmpty(user.PasswordHash));
        }

        public Task SetPasswordHashAsync(TUser user, string passwordHash, CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            cancellationToken.ThrowIfCancellationRequested();

            if (debugLog) { log.LogInformation("SetPasswordHashAsync"); }
            
            if (user == null)
            {
                throw new ArgumentNullException("user");
            }

            user.PasswordHash = passwordHash;
            user.LastPasswordChangedDate = DateTime.UtcNow;

            // I don't think this method is expected to
            // persist this change to the db
            // it seems to call this before calling Create

            // for some reason this is called before Create
            // so this mthod is actually doing the create
            //if (user.SiteGuid == Guid.Empty)
            //{
            //    user.SiteGuid = siteSettings.SiteGuid;
            //}
            //if (user.SiteId == -1)
            //{
            //    user.SiteId = siteSettings.SiteId;
            //}   
            //token.ThrowIfCancellationRequested();
            //user.PasswordHash = passwordHash;
            //userRepo.Save(user);

            return Task.FromResult(0);

        }

//#pragma warning restore 1998

        #endregion

        #region IUserLockoutStore

        
        

        /// <summary>
        /// Retrieves a flag indicating whether user lockout can enabled for the specified user.
        /// </summary>
        /// <param name="user">The user whose ability to be locked out should be returned.</param>
        /// <param name="cancellationToken">The <see cref="CancellationToken"/> used to propagate notifications that the operation should be canceled.</param>
        /// <returns>
        /// The <see cref="Task"/> that represents the asynchronous operation, true if a user can be locked out, otherwise false.
        /// </returns>
        public Task<bool> GetLockoutEnabledAsync(TUser user, CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            cancellationToken.ThrowIfCancellationRequested();
            if (debugLog) { log.LogInformation("GetLockoutEnabledAsync"); }
            
            if (user == null)
            {
                throw new ArgumentNullException("user");
            }

            //return Task.FromResult(user.IsLockedOut);
            return Task.FromResult(true); // any user can be locked out, maybe should not be posible to lockout some users like admin user
        }

        /// <summary>
        /// Gets the last <see cref="DateTimeOffset"/> a user's last lockout expired, if any.
        /// Any time in the past should be indicates a user is not locked out.
        /// </summary>
        /// <param name="user">The user whose lockout date should be retrieved.</param>
        /// <param name="cancellationToken">The <see cref="CancellationToken"/> used to propagate notifications that the operation should be canceled.</param>
        /// <returns>
        /// A <see cref="Task{TResult}"/> that represents the result of the asynchronous query, a <see cref="DateTimeOffset"/> containing the last time
        /// a user's lockout expired, if any.
        /// </returns>
        public Task<DateTimeOffset?> GetLockoutEndDateAsync(TUser user, CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            cancellationToken.ThrowIfCancellationRequested();
            if (debugLog) { log.LogInformation("GetLockoutEndDateAsync"); }
            
            if (user == null)
            {
                throw new ArgumentNullException("user");
            }

            DateTimeOffset? d;
            if (user.LockoutEndDateUtc != null)
            {
                if(user.LockoutEndDateUtc.Value == DateTime.MinValue) { return null; }

                d = new DateTimeOffset(user.LockoutEndDateUtc.Value);
                return Task.FromResult(d);
            }

            d = new DateTimeOffset(DateTime.UtcNow.AddDays(-2));
            return Task.FromResult(d);
        }


        /// <summary>
        /// Retrieves the current failed access count for the specified <paramref name="user"/>..
        /// </summary>
        /// <param name="user">The user whose failed access count should be retrieved.</param>
        /// <param name="cancellationToken">The <see cref="CancellationToken"/> used to propagate notifications that the operation should be canceled.</param>
        /// <returns>The <see cref="Task"/> that represents the asynchronous operation, containing the failed access count.</returns>
        public Task<int> GetAccessFailedCountAsync(TUser user, CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            cancellationToken.ThrowIfCancellationRequested();
            if (debugLog) { log.LogInformation("GetAccessFailedCountAsync"); }
            
            if (user == null)
            {
                throw new ArgumentNullException("user");
            }

            return Task.FromResult(user.AccessFailedCount);
        }


        /// <summary>
        /// Records that a failed access has occurred, incrementing the failed access count.
        /// </summary>
        /// <param name="user">The user whose cancellation count should be incremented.</param>
        /// <param name="cancellationToken">The <see cref="CancellationToken"/> used to propagate notifications that the operation should be canceled.</param>
        /// <returns>The <see cref="Task"/> that represents the asynchronous operation, containing the incremented failed access count.</returns>
        public async Task<int> IncrementAccessFailedCountAsync(TUser user, CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            cancellationToken.ThrowIfCancellationRequested();
            if (debugLog) { log.LogInformation("IncrementAccessFailedCountAsync"); }
            
            if (user == null)
            {
                throw new ArgumentNullException("user");
            }

            user.AccessFailedCount += 1;
            cancellationToken.ThrowIfCancellationRequested();
            await commands.UpdateFailedPasswordAttemptCount(user.Id, user.AccessFailedCount, cancellationToken);
            return user.AccessFailedCount;
        }

        /// <summary>
        /// Resets a user's failed access count.
        /// </summary>
        /// <param name="user">The user whose failed access count should be reset.</param>
        /// <param name="cancellationToken">The <see cref="CancellationToken"/> used to propagate notifications that the operation should be canceled.</param>
        /// <returns>The <see cref="Task"/> that represents the asynchronous operation.</returns>
        /// <remarks>This is typically called after the account is successfully accessed.</remarks>
        public async Task ResetAccessFailedCountAsync(TUser user, CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            cancellationToken.ThrowIfCancellationRequested();
            if (debugLog) { log.LogInformation("ResetAccessFailedCountAsync"); }
            
            if (user == null)
            {
                throw new ArgumentNullException("user");
            }

            user.AccessFailedCount = 0;
            // EF implementation doesn't save here
            // but we have to since our save doesn't update this
            // we have specific methods as shown here
            await commands.UpdateFailedPasswordAttemptCount(user.Id, user.AccessFailedCount, cancellationToken);

        }

        /// <summary>
        /// Set the flag indicating if the specified <paramref name="user"/> can be locked out..
        /// </summary>
        /// <param name="user">The user whose ability to be locked out should be set.</param>
        /// <param name="enabled">A flag indicating if lock out can be enabled for the specified <paramref name="user"/>.</param>
        /// <param name="cancellationToken">The <see cref="CancellationToken"/> used to propagate notifications that the operation should be canceled.</param>
        /// <returns>The <see cref="Task"/> that represents the asynchronous operation.</returns>
        public Task SetLockoutEnabledAsync(TUser user, bool enabled, CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            cancellationToken.ThrowIfCancellationRequested();
            //if (debugLog) { log.LogInformation("SetLockoutEnabledAsync"); }

            // I need to figure out where this is called from or if it is called from anywhere
            log.LogWarning("SetLockoutEnabledAsync");

            // I was confused about this property and tried to map it to IsLockedOut
            // but really IsLockedOut is determined only by the lockoutenddate
            // if it is null or less than now the user is not locked out
            // LockoutEnabled must be for a different purpose
            // like maybe it should not be possible for some users like admins to get locked out
            
            if (user == null)
            {
                throw new ArgumentNullException("user");
            }

            //bool result;
            // EF implementation doesn't save here but I think our save doesn't set lockout so we have to

            // this method is not supposed to do the actual lockout
            //if (enabled)
            //{
            //    result = await repo.LockoutAccount(user.UserGuid, cancellationToken);
            //}
            //else
            //{
            //    result = await repo.UnLockAccount(user.UserGuid, cancellationToken);
            //}

            return Task.FromResult(0);

        }

        /// <summary>
        /// Locks out a user until the specified end date has passed. Setting a end date in the past immediately unlocks a user.
        /// </summary>
        /// <param name="user">The user whose lockout date should be set.</param>
        /// <param name="lockoutEnd">The <see cref="DateTimeOffset"/> after which the <paramref name="user"/>'s lockout should end.</param>
        /// <param name="cancellationToken">The <see cref="CancellationToken"/> used to propagate notifications that the operation should be canceled.</param>
        /// <returns>The <see cref="Task"/> that represents the asynchronous operation.</returns>
        public Task SetLockoutEndDateAsync(TUser user, DateTimeOffset? lockoutEnd, CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            cancellationToken.ThrowIfCancellationRequested();
            if (debugLog) { log.LogInformation("SetLockoutEndDateAsync"); }
            
            if (user == null)
            {
                throw new ArgumentNullException("user");
            }

            //bool result;
            if (lockoutEnd.HasValue)
            {
                user.LockoutEndDateUtc = lockoutEnd.Value.DateTime;
                //if(user.LockoutEndDateUtc > DateTime.UtcNow)
                //{
                //    //result = await repo.LockoutAccount(user.UserGuid, cancellationToken);
                //    //user.IsLockedOut = true;
                    
                //}
                //else
                //{
                //    //result = await repo.UnLockAccount(user.UserGuid, cancellationToken);
                //    //user.IsLockedOut = false;
                //}

                //result = await repo.Save(user, cancellationToken);
            }
            else
            {
                user.LockoutEndDateUtc = null;
                //user.IsLockedOut = false;
                //result = await repo.UnLockAccount(user.UserGuid, cancellationToken);
                //result = await repo.Save(user, cancellationToken);
            }

            // EF implementation doesn't save here
            //bool result = await repo.Save(user);
            return Task.FromResult(0);
        }



        #endregion

        #region IUserClaimStore

        public async Task AddClaimsAsync(TUser user, IEnumerable<Claim> claims, CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            cancellationToken.ThrowIfCancellationRequested();

            if (debugLog) { log.LogInformation("AddClaimsAsync"); }
            
            if (user == null)
            {
                throw new ArgumentNullException("user");
            }

            Guid siteGuid = siteSettings.Id;
            if(multiTenantOptions.UseRelatedSitesMode) { siteGuid = multiTenantOptions.RelatedSiteGuid; }

            foreach (Claim claim in claims)
            {
                UserClaim userClaim = new UserClaim();
                userClaim.SiteId = siteGuid;
                userClaim.UserId = user.Id;
                userClaim.ClaimType = claim.Type;
                userClaim.ClaimValue = claim.Value;
                cancellationToken.ThrowIfCancellationRequested();
                await commands.CreateClaim(userClaim, cancellationToken);
            }
            

        }

        public async Task RemoveClaimsAsync(TUser user, IEnumerable<Claim> claims, CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            cancellationToken.ThrowIfCancellationRequested();
            if (debugLog) { log.LogInformation("RemoveClaimAsync"); }
            
            if (user == null)
            {
                throw new ArgumentNullException("user");
            }

            Guid siteGuid = siteSettings.Id;
            if (multiTenantOptions.UseRelatedSitesMode) { siteGuid = multiTenantOptions.RelatedSiteGuid; }

            foreach (Claim claim in claims)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await commands.DeleteClaimByUser(siteGuid, user.Id, claim.Type, cancellationToken);
            }
           
        }

        public async Task ReplaceClaimAsync(TUser user, Claim claim, Claim newClaim, CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            cancellationToken.ThrowIfCancellationRequested();
            if (debugLog) { log.LogInformation("ReplaceClaimAsync"); }
            
            if (user == null)
            {
                throw new ArgumentNullException("user");
            }

            Guid siteGuid = siteSettings.Id;
            if (multiTenantOptions.UseRelatedSitesMode) { siteGuid = multiTenantOptions.RelatedSiteGuid; }

            await commands.DeleteClaimByUser(siteGuid, user.Id, claim.Type, cancellationToken);

            UserClaim userClaim = new UserClaim();
            userClaim.SiteId = siteGuid;
            userClaim.UserId = user.Id;
            userClaim.ClaimType = newClaim.Type;
            userClaim.ClaimValue = newClaim.Value;
            cancellationToken.ThrowIfCancellationRequested();
            await commands.CreateClaim(userClaim, cancellationToken);

        }

        public async Task<IList<Claim>> GetClaimsAsync(TUser user, CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            cancellationToken.ThrowIfCancellationRequested();
            if (debugLog) { log.LogInformation("GetClaimsAsync"); }
            
            if (user == null)
            {
                throw new ArgumentNullException("user");
            }

            var claims = new List<Claim>();
            
            var siteGuid = siteSettings.Id;
            if (multiTenantOptions.UseRelatedSitesMode) { siteGuid = multiTenantOptions.RelatedSiteGuid; }

            var userClaims = await queries.GetClaimsByUser(siteGuid, user.Id, cancellationToken);
            foreach (UserClaim uc in userClaims)
            {
                Claim c = new Claim(uc.ClaimType, uc.ClaimValue);
                claims.Add(c);
            }

            return claims;

        }

        public async Task<IList<TUser>> GetUsersForClaimAsync(Claim claim, CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            cancellationToken.ThrowIfCancellationRequested();

            if (debugLog) { log.LogInformation("GetRolesAsync"); }
            
            if (claim == null)
            {
                throw new ArgumentNullException("claim");
            }

            var siteGuid = siteSettings.Id;
            if (multiTenantOptions.UseRelatedSitesMode) { siteGuid = multiTenantOptions.RelatedSiteGuid; }

            var users = await queries.GetUsersForClaim(siteGuid, claim.Type, claim.Value, cancellationToken);

            return (IList<TUser>)users;
        }

        #endregion

        #region IUserLoginStore

        public async Task AddLoginAsync(TUser user, UserLoginInfo login, CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            cancellationToken.ThrowIfCancellationRequested();

            if (debugLog) { log.LogInformation("AddLoginAsync"); }
            
            if (user == null)
            {
                throw new ArgumentNullException("user");
            }

            Guid siteGuid = siteSettings.Id;
            if (multiTenantOptions.UseRelatedSitesMode) { siteGuid = multiTenantOptions.RelatedSiteGuid; }

            UserLogin userlogin = new UserLogin();
            userlogin.SiteId = siteGuid;
            userlogin.UserId = user.Id;
            userlogin.LoginProvider = login.LoginProvider;
            userlogin.ProviderKey = login.ProviderKey;
            userlogin.ProviderDisplayName = login.ProviderDisplayName;

            cancellationToken.ThrowIfCancellationRequested();
            await commands.CreateLogin(userlogin, cancellationToken);

        }

        public async Task<TUser> FindByLoginAsync(string loginProvider, string providerKey, CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            cancellationToken.ThrowIfCancellationRequested();

            if (debugLog) { log.LogInformation("FindAsync called for " + loginProvider + " with providerKey " + providerKey); }

            log.LogInformation("FindAsync called for " + loginProvider + " with providerKey " + providerKey);
            
            Guid siteGuid = siteSettings.Id;
            if (multiTenantOptions.UseRelatedSitesMode) { siteGuid = multiTenantOptions.RelatedSiteGuid; }

            var userlogin = await queries.FindLogin(siteGuid, loginProvider, providerKey, cancellationToken);
            if (userlogin != null && userlogin.UserId != Guid.Empty)
            {
                log.LogInformation("FindAsync userLogin found for " + loginProvider + " with providerKey " + providerKey);

                
                cancellationToken.ThrowIfCancellationRequested();
                var siteUser = await queries.Fetch(siteGuid, userlogin.UserId, cancellationToken);
                if (siteUser != null)
                {
                    return (TUser)siteUser;
                }
                else
                {
                    log.LogInformation("FindAsync siteUser not found for " + loginProvider + " with providerKey " + providerKey);
                   
                }
            }
            else
            {
                log.LogInformation("FindAsync userLogin not found for " + loginProvider + " with providerKey " + providerKey);
            }

            return default(TUser);
        }

        public async Task<IList<UserLoginInfo>> GetLoginsAsync(TUser user, CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            cancellationToken.ThrowIfCancellationRequested();
            if (debugLog) { log.LogInformation("GetLoginsAsync"); }
            
            if (user == null)
            {
                throw new ArgumentNullException("user");
            }

            var logins = new List<UserLoginInfo>();
            
            var siteGuid = siteSettings.Id;
            if (multiTenantOptions.UseRelatedSitesMode) { siteGuid = multiTenantOptions.RelatedSiteGuid; }

            var userLogins = await queries.GetLoginsByUser(siteGuid, user.Id, cancellationToken);
            foreach (UserLogin ul in userLogins)
            {
                var l = new UserLoginInfo(ul.LoginProvider, ul.ProviderKey, ul.ProviderDisplayName);
                logins.Add(l);
            }


            return logins;
        }

        public async Task RemoveLoginAsync(TUser user, string loginProvider, string providerKey, CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            cancellationToken.ThrowIfCancellationRequested();
            if (debugLog) { log.LogInformation("RemoveLoginAsync"); }
            
            if (user == null)
            {
                throw new ArgumentNullException("user");
            }
            
            var siteGuid = siteSettings.Id;
            if (multiTenantOptions.UseRelatedSitesMode) { siteGuid = multiTenantOptions.RelatedSiteGuid; }

            await commands.DeleteLogin(
                siteGuid,
                user.Id,
                loginProvider,
                providerKey,
                cancellationToken
                );


        }

        #endregion

        #region IUserTwoFactorStore


        /// <summary>
        /// Returns a flag indicating whether the specified <paramref name="user "/>has two factor authentication enabled or not,
        /// as an asynchronous operation.
        /// </summary>
        /// <param name="user">The user whose two factor authentication enabled status should be set.</param>
        /// <param name="cancellationToken">The <see cref="CancellationToken"/> used to propagate notifications that the operation should be canceled.</param>
        /// <returns>
        /// The <see cref="Task"/> that represents the asynchronous operation, containing a flag indicating whether the specified 
        /// <paramref name="user "/>has two factor authentication enabled or not.
        /// </returns>
        public Task<bool> GetTwoFactorEnabledAsync(TUser user, CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            cancellationToken.ThrowIfCancellationRequested();
            if (debugLog) { log.LogInformation("GetTwoFactorEnabledAsync"); }
            
            if (user == null)
            {
                throw new ArgumentNullException("user");
            }

            return Task.FromResult(user.TwoFactorEnabled);
        }

        /// <summary>
        /// Sets a flag indicating whether the specified <paramref name="user "/>has two factor authentication enabled or not,
        /// as an asynchronous operation.
        /// </summary>
        /// <param name="user">The user whose two factor authentication enabled status should be set.</param>
        /// <param name="enabled">A flag indicating whether the specified <paramref name="user"/> has two factor authentication enabled.</param>
        /// <param name="cancellationToken">The <see cref="CancellationToken"/> used to propagate notifications that the operation should be canceled.</param>
        /// <returns>The <see cref="Task"/> that represents the asynchronous operation.</returns>
        public Task SetTwoFactorEnabledAsync(TUser user, bool enabled, CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            cancellationToken.ThrowIfCancellationRequested();
            if (debugLog) { log.LogInformation("SetTwoFactorEnabledAsync"); }
            
            if (user == null)
            {
                throw new ArgumentNullException("user");
            }

            user.TwoFactorEnabled = enabled;
            
            //EF implementation doesn't save here
            //bool result = await repo.Save(user);

            return Task.FromResult(0);
        }


        #endregion

        #region IUserPhoneNumberStore

       
        public Task<string> GetPhoneNumberAsync(TUser user, CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            cancellationToken.ThrowIfCancellationRequested();
            if (debugLog) { log.LogInformation("GetPhoneNumberAsync"); }
            
            if (user == null)
            {
                throw new ArgumentNullException("user");
            }

            return Task.FromResult(user.PhoneNumber);
        }

        public Task<bool> GetPhoneNumberConfirmedAsync(TUser user, CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            cancellationToken.ThrowIfCancellationRequested();
            if (debugLog) { log.LogInformation("GetPhoneNumberConfirmedAsync"); }
            
            if (user == null)
            {
                throw new ArgumentNullException("user");
            }

            return Task.FromResult(user.PhoneNumberConfirmed);
        }



        public Task SetPhoneNumberAsync(TUser user, string phoneNumber, CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            cancellationToken.ThrowIfCancellationRequested();
            if (debugLog) { log.LogInformation("SetPhoneNumberAsync"); }
            
            if (user == null)
            {
                throw new ArgumentNullException("user");
            }

            user.PhoneNumber = phoneNumber;
            // EF implementation doesn't save here
            //bool result = await repo.Save(user);
            return Task.FromResult(0);
        }

        public Task SetPhoneNumberConfirmedAsync(TUser user, bool confirmed, CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            cancellationToken.ThrowIfCancellationRequested();

            if (debugLog) { log.LogInformation("SetPhoneNumberConfirmedAsync"); }
            
            if (user == null)
            {
                throw new ArgumentNullException("user");
            }

            user.PhoneNumberConfirmed = confirmed;
            // EF implementation does not save here
            //bool result = await repo.Save(user);
            return Task.FromResult(0);

        }

        #endregion

        #region IUserRoleStore

        public async Task AddToRoleAsync(TUser user, string role, CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            cancellationToken.ThrowIfCancellationRequested();

            if (debugLog) { log.LogInformation("AddToRoleAsync"); }
            
            if (user == null)
            {
                throw new ArgumentNullException("user");
            }
            
            var siteGuid = siteSettings.Id;
            if (multiTenantOptions.UseRelatedSitesMode) { siteGuid = multiTenantOptions.RelatedSiteGuid; }

            var siteRole = await queries.FetchRole(siteGuid, role, cancellationToken);
            if (siteRole != null)
            {
                await commands.AddUserToRole(siteRole.Id, user.Id, cancellationToken);
            }

        }

        public async Task<IList<string>> GetRolesAsync(TUser user, CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            cancellationToken.ThrowIfCancellationRequested();

            if (debugLog) { log.LogInformation("GetRolesAsync"); }
            
            if (user == null)
            {
                throw new ArgumentNullException("user");
            }

            var roles = new List<string>();
            
            var siteGuid = siteSettings.Id;
            if (multiTenantOptions.UseRelatedSitesMode) { siteGuid = multiTenantOptions.RelatedSiteGuid; }

            roles = await queries.GetUserRoles(siteGuid, user.Id, cancellationToken);

            return roles;
        }

        public async Task<bool> IsInRoleAsync(TUser user, string role, CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            cancellationToken.ThrowIfCancellationRequested();

            if (debugLog) { log.LogInformation("IsInRoleAsync"); }

           
            if (user == null)
            {
                throw new ArgumentNullException("user");
            }

            var result = false;
            var roles = await queries.GetUserRoles(siteSettings.Id, user.Id, cancellationToken);

            foreach (string r in roles)
            {
                if (string.Equals(r, role, StringComparison.CurrentCultureIgnoreCase))
                {
                    result = true;
                    break;
                }
            }

            return result;
        }

        public async Task<IList<TUser>> GetUsersInRoleAsync(string role, CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            cancellationToken.ThrowIfCancellationRequested();
            if (debugLog) { log.LogInformation("GetRolesAsync"); }
           
            var siteGuid = siteSettings.Id;
            if (multiTenantOptions.UseRelatedSitesMode) { siteGuid = multiTenantOptions.RelatedSiteGuid; }

            var users = await queries.GetUsersInRole(siteGuid, role, cancellationToken);

            return (IList<TUser>)users; 
        }

        public async Task RemoveFromRoleAsync(TUser user, string role, CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            cancellationToken.ThrowIfCancellationRequested();
            if (debugLog) { log.LogInformation("RemoveFromRoleAsync"); }
            if (user == null)
            {
                throw new ArgumentNullException("user");
            }
            
            var siteGuid = siteSettings.Id;
            if (multiTenantOptions.UseRelatedSitesMode) { siteGuid = multiTenantOptions.RelatedSiteGuid; }

            var siteRole = await queries.FetchRole(siteGuid, role, cancellationToken);
            
            if (siteRole != null)
            {  
                await commands.RemoveUserFromRole(siteRole.Id, user.Id, cancellationToken);
            }

        }

        #endregion

        #region Helpers

        public async Task<string> SuggestLoginNameFromEmail(Guid siteGuid, string email)
        {
            if (multiTenantOptions.UseRelatedSitesMode) { siteGuid = multiTenantOptions.RelatedSiteGuid; }

            var login = email.Substring(0, email.IndexOf("@"));
            var offset = 1;
            // don't think we should make this async inside a loop
            while (await queries.LoginExistsInDB(siteGuid, login).ConfigureAwait(false))
            {
                offset += 1;
                login = email.Substring(0, email.IndexOf("@")) + offset.ToInvariantString();

            }

            return login;
        }

        #endregion

        private void ThrowIfDisposed()
        {
            if (disposedValue)
            {
                throw new ObjectDisposedException(GetType().Name);
            }
        }


        #region IDisposable Support
        private bool disposedValue = false; // To detect redundant calls

        void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    // TODO: dispose managed state (managed objects).
                }

                // TODO: free unmanaged resources (unmanaged objects) and override a finalizer below.
                // TODO: set large fields to null.

                disposedValue = true;
            }
        }

        // TODO: override a finalizer only if Dispose(bool disposing) above has code to free unmanaged resources.
        // ~SiteRoleStore() {
        //   // Do not change this code. Put cleanup code in Dispose(bool disposing) above.
        //   Dispose(false);
        // }

        // This code added to correctly implement the disposable pattern.
        public void Dispose()
        {
            // Do not change this code. Put cleanup code in Dispose(bool disposing) above.
            Dispose(true);
            // TODO: uncomment the following line if the finalizer is overridden above.
            // GC.SuppressFinalize(this);
        }
        #endregion

    }
}
