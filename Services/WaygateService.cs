using ProjectM;
using ProjectM.Network;
using ProjectM.Terrain;
using ProjectM.Shared;
using Stunlock.Core;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using BepInEx;
using System.IO;
using System.Text.Json;

namespace FatedGates.Services;
class WaygateService
{
    const float UnlockDistance = 25f;
    readonly EntityQuery connectedUserQuery;
    readonly EntityQuery waypointQuery;
    readonly EntityQuery spawnedWaypointQuery;

    readonly Dictionary<Entity, List<NetworkId>> unlockedSpawnedWaypoints = [];
    readonly HashSet<NetworkId> autoUnlockWaypoints = [];

    readonly string currentSaveName;
    readonly string saveScopedConfigDir;
    readonly string publicWaygatesPath;

    public class PublicWaygate
    {
        public string Name { get; set; } = "Public Waygate";
        public float WorldX { get; set; }
        public float WorldY { get; set; }
        public float WorldZ { get; set; }
    }

    readonly List<PublicWaygate> publicWaygates = [];

    public WaygateService()
    {
        var spawnedWaypointQueryBuilder = new EntityQueryBuilder(Allocator.Temp)
            .AddAll(ComponentType.ReadOnly<ChunkWaypoint>())
            .AddAll(ComponentType.ReadOnly<SpawnedBy>())
            .WithOptions(EntityQueryOptions.IncludeDisabled);
        spawnedWaypointQuery = Core.EntityManager.CreateEntityQuery(ref spawnedWaypointQueryBuilder);
        spawnedWaypointQueryBuilder.Dispose();

        var connectedUserQueryBuilder = new EntityQueryBuilder(Allocator.Temp)
            .AddAll(ComponentType.ReadOnly<IsConnected>())
            .AddAll(ComponentType.ReadOnly<User>());

        connectedUserQuery = Core.EntityManager.CreateEntityQuery(ref connectedUserQueryBuilder);
        connectedUserQueryBuilder.Dispose();

        var waypointQueryBuilder = new EntityQueryBuilder(Allocator.Temp)
            .AddAll(ComponentType.ReadOnly<ChunkWaypoint>())
            .WithOptions(EntityQueryOptions.IncludeDisabled);
        waypointQuery = Core.EntityManager.CreateEntityQuery(ref waypointQueryBuilder);
        waypointQueryBuilder.Dispose();

        currentSaveName = GetCurrentSaveName();

        saveScopedConfigDir = Path.Combine(
            BepInEx.Paths.ConfigPath,
            "FatedGates",
            currentSaveName
        );

        publicWaygatesPath = Path.Combine(saveScopedConfigDir, "PublicWaygates.json");

        Directory.CreateDirectory(saveScopedConfigDir);

        Core.Log.LogInfo($"Using save-scoped FatedGates config directory: {saveScopedConfigDir}");

        LoadPublicWaygates();

        if (Core.ServerGameSettingsSystem.Settings.AllWaypointsUnlocked)
        {
            return;
        }

        Core.StartCoroutine(ResolvePublicWaygatesWhenReady());
        Core.StartCoroutine(CheckForWaypointUnlocks());
    }

    void LoadPublicWaygates()
    {
        try
        {
            if (!File.Exists(publicWaygatesPath))
            {
                SavePublicWaygates();
                return;
            }

            var json = File.ReadAllText(publicWaygatesPath);
            var loaded = JsonSerializer.Deserialize<List<PublicWaygate>>(json);

            publicWaygates.Clear();

            if (loaded != null)
            {
                publicWaygates.AddRange(loaded);
            }

            Core.Log.LogInfo($"Loaded {publicWaygates.Count} public waygates.");
        }
        catch (Exception e)
        {
            Core.Log.LogError($"Failed to load public waygates: {e}");
        }
    }

    void SavePublicWaygates()
    {
        try
        {
            var directory = Path.GetDirectoryName(publicWaygatesPath);

            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(
                publicWaygates,
                new JsonSerializerOptions { WriteIndented = true }
            );

            File.WriteAllText(publicWaygatesPath, json);
            Core.Log.LogInfo($"Saved {publicWaygates.Count} public waygates.");
        }
        catch (Exception e)
        {
            Core.Log.LogError($"Failed to save public waygates: {e}");
        }
    }

    public void SavePublicWaygate(Entity waygateEntity, string name = "Public Waygate")
    {
        var pos = waygateEntity.Read<Translation>().Value;

        var alreadySaved = publicWaygates.Any(existing =>
        {
            var existingPos = new float3(existing.WorldX, existing.WorldY, existing.WorldZ);
            return math.distance(existingPos, pos) < 2f;
        });

        if (alreadySaved)
        {
            Core.Log.LogInfo($"Public waygate near X={pos.x:F2}, Y={pos.y:F2}, Z={pos.z:F2} is already saved.");
        }
        else
        {
            publicWaygates.Add(new PublicWaygate
            {
                Name = name,
                WorldX = pos.x,
                WorldY = pos.y,
                WorldZ = pos.z
            });

            SavePublicWaygates();

            Core.Log.LogInfo($"Saved public waygate '{name}' at X={pos.x:F2}, Y={pos.y:F2}, Z={pos.z:F2}.");
        }

        var networkId = waygateEntity.Read<NetworkId>();

        if (networkId != NetworkId.Empty)
        {
            autoUnlockWaypoints.Add(networkId);
            UnlockWaypointForAllConnectedUsers(networkId);
        }
    }

    IEnumerator ResolvePublicWaygatesWhenReady()
    {
        if (publicWaygates.Count == 0)
        {
            Core.Log.LogInfo("No saved public waygates to resolve.");
            yield break;
        }

        const int maxAttempts = 10;
        var wait = new WaitForSeconds(1f);

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            var spawnedWaygates = spawnedWaypointQuery.ToEntityArray(Allocator.Temp);
            var count = spawnedWaygates.Length;
            spawnedWaygates.Dispose();

            if (count > 0)
            {
                Core.Log.LogInfo($"Resolving public waygates after {attempt} attempt(s); spawned waygate count={count}.");
                ResolvePublicWaygates();
                yield break;
            }

            Core.Log.LogInfo($"Public waygate resolve attempt {attempt}: no spawned waygates found yet.");
            yield return wait;
        }

        Core.Log.LogWarning("Failed to resolve public waygates: no spawned waygates found after waiting.");
    }

    public void ResolvePublicWaygates()
    {
        autoUnlockWaypoints.Clear();

        var spawnedWaygates = spawnedWaypointQuery.ToEntityArray(Allocator.Temp);
        var spawnedWaygatesArray = spawnedWaygates.ToArray();

        var unresolvedPublicWaygates = new List<PublicWaygate>();

        try
        {
            foreach (var publicWaygate in publicWaygates)
            {
                var savedPos = new float3(
                    publicWaygate.WorldX,
                    publicWaygate.WorldY,
                    publicWaygate.WorldZ
                );

                var closestWaypoint = spawnedWaygatesArray
                    .OrderBy(x => math.distance(savedPos, x.Read<Translation>().Value))
                    .FirstOrDefault();

                if (closestWaypoint == Entity.Null)
                {
                    Core.Log.LogWarning(
                        $"Could not resolve public waygate '{publicWaygate.Name}'; no spawned waygates found. " +
                        "Removing stale public waygate entry."
                    );

                    unresolvedPublicWaygates.Add(publicWaygate);
                    continue;
                }

                var distance = math.distance(savedPos, closestWaypoint.Read<Translation>().Value);

                if (distance > 10f)
                {
                    Core.Log.LogWarning(
                        $"Could not resolve public waygate '{publicWaygate.Name}'; closest spawned waygate was {distance:F2} units away. " +
                        "Removing stale public waygate entry."
                    );

                    unresolvedPublicWaygates.Add(publicWaygate);
                    continue;
                }

                var networkId = closestWaypoint.Read<NetworkId>();

                if (networkId == NetworkId.Empty)
                {
                    Core.Log.LogWarning(
                        $"Could not resolve public waygate '{publicWaygate.Name}'; NetworkId was empty. " +
                        "Keeping entry for now; it may resolve later."
                    );

                    continue;
                }

                autoUnlockWaypoints.Add(networkId);

                Core.Log.LogInfo(
                    $"Resolved public waygate '{publicWaygate.Name}' to {networkId} at distance {distance:F2}."
                );
            }
        }
        finally
        {
            spawnedWaygates.Dispose();
        }

        if (unresolvedPublicWaygates.Count > 0)
        {
            foreach (var unresolved in unresolvedPublicWaygates)
            {
                publicWaygates.Remove(unresolved);
            }

            SavePublicWaygates();

            Core.Log.LogInfo(
                $"Removed {unresolvedPublicWaygates.Count} stale public waygate entr" +
                $"{(unresolvedPublicWaygates.Count == 1 ? "y" : "ies")} from PublicWaygates.json."
            );
        }

        UnlockAutoWaygatesForAllConnectedUsers();
    }

    public bool GetClosestSpawnedWaygate(
        float3 position,
        out Entity closestWaygate,
        out float distance
    )
    {
        closestWaygate = Entity.Null;
        distance = float.MaxValue;

        var spawnedWaygates = spawnedWaypointQuery.ToEntityArray(Allocator.Temp);

        try
        {
            foreach (var waygate in spawnedWaygates)
            {
                var waygatePos = waygate.Read<Translation>().Value;
                var currentDistance = math.distance(position, waygatePos);

                if (currentDistance < distance)
                {
                    distance = currentDistance;
                    closestWaygate = waygate;
                }
            }

            if (closestWaygate == Entity.Null)
            {
                return false;
            }

            return true;
        }
        finally
        {
            spawnedWaygates.Dispose();
        }
    }

    List<NetworkId> InitializeUnlockedWaypoints(Entity userEntity)
    {
        var unlockedUserSpawnedWaypoints = new List<NetworkId>();
        unlockedSpawnedWaypoints.Add(userEntity, unlockedUserSpawnedWaypoints);

        var unlockedWaypoints = Core.EntityManager.GetBuffer<UnlockedWaypointElement>(userEntity);
        var spawnedWaypoints = spawnedWaypointQuery.ToEntityArray(Allocator.Temp);
        var spawnedWaypointsArray = spawnedWaypoints.ToArray();
        spawnedWaypoints.Dispose();

        foreach (var waypoint in unlockedWaypoints)
        {
            if (spawnedWaypointsArray.Any(x => x.Read<NetworkId>() == waypoint.Waypoint))
            {
                unlockedUserSpawnedWaypoints.Add(waypoint.Waypoint);
            }
        }

        return unlockedUserSpawnedWaypoints;
    }

    public bool CreateWaygate(
        Entity character,
        PrefabGUID waypointPrefabGUID,
        bool autoUnlockForAllConnectedUsers = false,
        string publicWaygateName = "Public Waygate"
    )
    {
        if (!Core.PrefabCollection._PrefabGuidToEntityMap.TryGetValue(waypointPrefabGUID, out var waypointPrefab))
        {
            Core.Log.LogError($"Failed to find {waypointPrefabGUID.LookupName()} Prefab entity");
            return false;
        }

        var pos = character.Read<Translation>().Value;
        var chunk = pos.GetChunk();

        var waypoints = waypointQuery.ToEntityArray(Allocator.Temp);
        try
        {
            foreach (var waypoint in waypoints)
            {
                if (waypoint.Has<CastleWorkstation>())
                    continue;

                if (waypoint.GetChunk() == chunk)
                    return false;
            }
        }
        finally
        {
            waypoints.Dispose();
        }

        var rot = character.Read<Rotation>().Value;

        var newWaypoint = Core.EntityManager.Instantiate(waypointPrefab);

        newWaypoint.Write(new Translation { Value = pos });
        newWaypoint.Write(new Rotation { Value = rot });
        newWaypoint.Add<SpawnedBy>();
        newWaypoint.Write(new SpawnedBy { Value = character });

        if (autoUnlockForAllConnectedUsers)
        {
            Core.StartCoroutine(InitializeAutoUnlockWaygateAfterSpawn(newWaypoint, publicWaygateName));
        }

        return true;
    }

    public bool TeleportToClosestWaygate(Entity character)
    {
        var pos = character.Read<Translation>().Value;
        var spawnedWaypoints = spawnedWaypointQuery.ToEntityArray(Allocator.Temp);
        var closestWaypoint = spawnedWaypoints.ToArray().OrderBy(x => math.distance(pos, x.Read<Translation>().Value)).FirstOrDefault();
        spawnedWaypoints.Dispose();
        if (closestWaypoint == Entity.Null) return false;

        var waypointPos = closestWaypoint.Read<Translation>().Value;
        var waypointRot = closestWaypoint.Read<Rotation>().Value;

        character.Write(new Translation { Value = waypointPos });
        character.Write(new LastTranslation { Value = waypointPos });
        character.Write(new Rotation { Value = waypointRot });
        return true;
    }

    public void UnlockWaypoint(Entity userEntity, NetworkId waypointNetworkId)
    {
        if (waypointNetworkId == NetworkId.Empty)
        {
            Core.Log.LogError("Attempted to unlock an empty waypoint");
            return;
        }

        if (!unlockedSpawnedWaypoints.TryGetValue(userEntity, out var unlockedWaypoints))
        {
            unlockedWaypoints = InitializeUnlockedWaypoints(userEntity);
        }

        if (unlockedWaypoints.Contains(waypointNetworkId))
        {
            return;
        }

        var unlockedWaypointElements = Core.EntityManager.GetBuffer<UnlockedWaypointElement>(userEntity);

        foreach (var unlockedWaypoint in unlockedWaypointElements)
        {
            if (unlockedWaypoint.Waypoint == waypointNetworkId)
            {
                unlockedWaypoints.Add(waypointNetworkId);
                return;
            }
        }

        Core.Log.LogInfo($"Waypoint {waypointNetworkId} unlocked for {userEntity.Read<User>().CharacterName}");

        unlockedWaypointElements.Add(new UnlockedWaypointElement
        {
            Waypoint = waypointNetworkId
        });

        unlockedWaypoints.Add(waypointNetworkId);
    }

    IEnumerator CheckForWaypointUnlocks()
    {
        var timeBetweenChecks = new WaitForSeconds(1f);
        while (true)
        {
            yield return timeBetweenChecks;
            var spawnedWaygates = spawnedWaypointQuery.ToEntityArray(Allocator.Temp);

            if (spawnedWaygates.Length == 0)
            {
                spawnedWaygates.Dispose();
                continue;
            }

            var connectedUsers = connectedUserQuery.ToEntityArray(Allocator.Temp);
            foreach (var userEntity in connectedUsers)
            {
                var user = userEntity.Read<User>();
                if (!unlockedSpawnedWaypoints.TryGetValue(userEntity, out var unlockedWaypoints))
                {
                    unlockedWaypoints = InitializeUnlockedWaypoints(userEntity);
                }
                
                var characterEntity = user.LocalCharacter.GetEntityOnServer();
                if (characterEntity == Entity.Null) continue;

                var pos = characterEntity.Read<Translation>().Value;
                foreach (var waygate in spawnedWaygates)
                {
                    var waygateNetworkId = waygate.Read<NetworkId>();
                    if (unlockedWaypoints.Contains(waygateNetworkId)) continue;

                    var waypointPos = waygate.Read<Translation>().Value;
                    if (math.distance(pos, waypointPos) < UnlockDistance)
                    {
                        UnlockWaypoint(userEntity, waygateNetworkId);
                    }
                }
            }

            connectedUsers.Dispose();
            spawnedWaygates.Dispose();
        }
    }

    public bool DestroyWaygate(Entity senderCharacterEntity, int publicWaygateIndex = 0)
    {
        Entity waypointToDestroy = Entity.Null;
        int savedPublicWaygateIndexToRemove = -1;
        const float DISTANCE_TO_DESTROY = 10f;
        float3 findClosestFromPosition;

        if (publicWaygateIndex > 0)
        {
            savedPublicWaygateIndexToRemove = publicWaygateIndex - 1;

            if (savedPublicWaygateIndexToRemove < 0 || savedPublicWaygateIndexToRemove >= publicWaygates.Count)
            {
                Core.Log.LogWarning($"Invalid public waygate index {publicWaygateIndex}.");
                return false;
            }

            var savedPublicWaygate = publicWaygates[savedPublicWaygateIndexToRemove];

            findClosestFromPosition = new float3(
                savedPublicWaygate.WorldX,
                savedPublicWaygate.WorldY,
                savedPublicWaygate.WorldZ
            );
        }
        else
        {
            findClosestFromPosition = senderCharacterEntity.Read<Translation>().Value;
        }

         GetClosestSpawnedWaygate(
            findClosestFromPosition,
            out waypointToDestroy,
            out var distance
        );

        if (waypointToDestroy == Entity.Null)
        {
            Core.Log.LogWarning("Could not find spawned waygate to destroy.");
            return false;
        }

        if (distance > DISTANCE_TO_DESTROY)
        {
            Core.Log.LogWarning($"Closest spawned waygate {distance} away, move within {DISTANCE_TO_DESTROY} to destroy");
            return false;
        }

        if (publicWaygateIndex == 0)
        {
            var waypointPos = waypointToDestroy.Read<Translation>().Value;

            savedPublicWaygateIndexToRemove = publicWaygates.FindIndex(saved => {
                var savedPos = new float3(saved.WorldX, saved.WorldY, saved.WorldZ);
                return math.distance(savedPos, waypointPos) < 2f;
            });
        }

        var networkId = waypointToDestroy.Read<NetworkId>();

        if (networkId != NetworkId.Empty)
        {
            autoUnlockWaypoints.Remove(networkId);
        }

        if (savedPublicWaygateIndexToRemove >= 0)
        {
            var removed = publicWaygates[savedPublicWaygateIndexToRemove];
            publicWaygates.RemoveAt(savedPublicWaygateIndexToRemove);
            SavePublicWaygates();

            Core.Log.LogInfo(
                $"Removed saved public waygate '{removed.Name}' " +
                $"at X={removed.WorldX:F2}, Y={removed.WorldY:F2}, Z={removed.WorldZ:F2}."
            );
        }

        DestroyUtility.Destroy(Core.EntityManager, waypointToDestroy);
        return true;
    }

    public void UnlockWaypointForAllConnectedUsers(NetworkId waypointNetworkId)
    {
        if (waypointNetworkId == NetworkId.Empty)
        {
            Core.Log.LogWarning("Attempted to unlock empty waypoint for connected users.");
            return;
        }

        var connectedUsers = connectedUserQuery.ToEntityArray(Allocator.Temp);

        try
        {
            foreach (var userEntity in connectedUsers)
            {
                if (userEntity == Entity.Null) continue;
                if (!userEntity.Has<User>()) continue;

                UnlockWaypoint(userEntity, waypointNetworkId);
            }
        }
        finally
        {
            connectedUsers.Dispose();
        }
    }

    public void UnlockAutoWaygatesForUser(Entity userEntity)
    {
        if (userEntity == Entity.Null)
        {
            Core.Log.LogWarning("Attempted to unlock auto waygates for Entity.Null");
            return;
        }

        if (!userEntity.Has<User>())
        {
            Core.Log.LogWarning($"Attempted to unlock auto waygates for non-user entity {userEntity}");
            return;
        }

        Core.Log.LogInfo($"Auto-unlock waygate count: {autoUnlockWaypoints.Count} for user for {userEntity.Read<User>().CharacterName}");

        if (Core.ServerGameSettingsSystem.Settings.AllWaypointsUnlocked)
        {
            // AllWaypointsUnlocked is enabled; skipping auto-unlock logic.
            return;
        }

        foreach (var waypointNetworkId in autoUnlockWaypoints)
        {
            Core.Log.LogInfo($"Auto-unlocking waypoint {waypointNetworkId} for {userEntity.Read<User>().CharacterName}");
            UnlockWaypoint(userEntity, waypointNetworkId);
        }
    }

    public void UnlockAutoWaygatesForAllConnectedUsers()
    {
        var connectedUsers = connectedUserQuery.ToEntityArray(Allocator.Temp);

        try
        {
            foreach (var userEntity in connectedUsers)
            {
                UnlockAutoWaygatesForUser(userEntity);
            }
        }
        finally
        {
            connectedUsers.Dispose();
        }
    }

    IEnumerator InitializeAutoUnlockWaygateAfterSpawn(Entity waygate, string name)
    {
        yield return new WaitForSeconds(1f);

        if (waygate == Entity.Null || !Core.EntityManager.Exists(waygate))
        {
            Core.Log.LogWarning("Auto-unlock waygate no longer exists after creation delay.");
            yield break;
        }

        if (!waygate.Has<NetworkId>())
        {
            Core.Log.LogWarning("Auto-unlock waygate does not have NetworkId after creation delay.");
            yield break;
        }

        var waygateNetworkId = waygate.Read<NetworkId>();

        if (waygateNetworkId == NetworkId.Empty)
        {
            Core.Log.LogWarning("Auto-unlock waygate still has empty NetworkId after creation delay.");
            yield break;
        }

        SavePublicWaygate(waygate, name);

        Core.Log.LogInfo($"Auto-unlock waygate registered: Entity={waygate}, NetworkId={waygateNetworkId}");
    }

    public string GetPublicWaygateListText()
    {
        if (publicWaygates.Count == 0)
        {
            return "No saved public waygates.";
        }

        var lines = new List<string>
    {
        $"Saved public waygates ({publicWaygates.Count}):"
    };

        for (var i = 0; i < publicWaygates.Count; i++)
        {
            var waygate = publicWaygates[i];

            lines.Add(
                $"{i + 1}. {waygate.Name} " +
                $"at X={waygate.WorldX:F2}, Y={waygate.WorldY:F2}, Z={waygate.WorldZ:F2}"
            );
        }

        return string.Join("\n", lines);
    }

    string GetCurrentSaveName()
    {
        var args = Environment.GetCommandLineArgs();

        for (var i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], "-saveName", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(args[i], "-serverSaveName", StringComparison.OrdinalIgnoreCase))
            {
                return SanitizeSaveName(args[i + 1]);
            }
        }

        var envSaveName = Environment.GetEnvironmentVariable("VR_SAVE_NAME");
        if (!string.IsNullOrWhiteSpace(envSaveName))
        {
            return SanitizeSaveName(envSaveName);
        }

        Core.Log.LogWarning("Could not determine active save name from command line or VR_SAVE_NAME. Using 'default'.");

        return "default";
    }

    string SanitizeSaveName(string saveName)
    {
        foreach (var invalidChar in Path.GetInvalidFileNameChars())
        {
            saveName = saveName.Replace(invalidChar, '_');
        }

        saveName = saveName.Trim();

        return string.IsNullOrWhiteSpace(saveName) ? "default" : saveName;
    }
}
