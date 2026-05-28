using System;
using HarmonyLib;
using ProjectM;
using ProjectM.Network;
using Stunlock.Network;
using Unity.Entities;

namespace FatedGates.Patches;

[HarmonyPatch(typeof(ServerBootstrapSystem), nameof(ServerBootstrapSystem.OnUserConnected))]
public static class OnUserConnected_Patch
{
    public static void Prefix(ServerBootstrapSystem __instance, NetConnectionId netConnectionId)
    {
        try
        {
            Core.Log.LogInfo("OnUserConnected PREFIX fired.");

            if (!__instance._NetEndPointToApprovedUserIndex.ContainsKey(netConnectionId))
            {
                Core.Log.LogInfo("PREFIX: netConnectionId not found in _NetEndPointToApprovedUserIndex yet.");
                return;
            }

            var userIndex = __instance._NetEndPointToApprovedUserIndex[netConnectionId];
            var serverClient = __instance._ApprovedUsersLookup[userIndex];
            var userEntity = serverClient.UserEntity;

            Core.Log.LogInfo($"PREFIX: resolved user entity {userEntity}");

            if (userEntity == Entity.Null || !Core.EntityManager.Exists(userEntity))
            {
                Core.Log.LogInfo("PREFIX: user entity is null or does not exist.");
                return;
            }

            Core.Log.LogInfo($"PREFIX: applying public reveal zones for {userEntity.Read<User>().CharacterName}");
            Core.RevealService.RevealAllPublicZonesForUser(userEntity);
        }
        catch (Exception e)
        {
            Core.Log.LogWarning($"OnUserConnected PREFIX test failed: {e.Message}");
        }
    }

    public static void Postfix(ServerBootstrapSystem __instance, NetConnectionId netConnectionId)
    {
        try
        {
            if (Core.WaygateService == null)
            {
                Core.InitializeAfterLoaded();
            }

            var userIndex = __instance._NetEndPointToApprovedUserIndex[netConnectionId];
            var serverClient = __instance._ApprovedUsersLookup[userIndex];
            var userEntity = serverClient.UserEntity;

            Core.WaygateService.UnlockAutoWaygatesForUser(userEntity);
        }
        catch (Exception e)
        {
            Core.Log.LogError(
                $"Failure in {nameof(ServerBootstrapSystem.OnUserConnected)} waygate unlock patch\n" +
                $"Message: {e.Message} Inner: {e.InnerException?.Message}\n\n" +
                $"Stack: {e.StackTrace}\n" +
                $"Inner Stack: {e.InnerException?.StackTrace}"
            );
        }
    }
}