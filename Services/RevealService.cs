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

class RevealService
{
    readonly List<PublicRevealZone> publicRevealZones = [];

    readonly string currentSaveName;
    readonly string saveScopedConfigDir;
    readonly string publicRevealZonesPath;

    public RevealService()
    {
        currentSaveName = GetCurrentSaveName();

        saveScopedConfigDir = Path.Combine(
            BepInEx.Paths.ConfigPath,
            "FatedGates",
            currentSaveName
        );

        publicRevealZonesPath = Path.Combine(saveScopedConfigDir, "PublicRevealZones.json");

        Directory.CreateDirectory(saveScopedConfigDir);

        LoadPublicRevealZones();
    }

    public class PublicRevealZone
    {
        public string Name { get; set; } = "Public Reveal Zone";
        public int X { get; set; }
        public int Y { get; set; }
        public int Radius { get; set; } = 1;
    }

    void LoadPublicRevealZones()
    {
        try
        {
            if (!File.Exists(publicRevealZonesPath))
            {
                SavePublicRevealZones();
                return;
            }

            var json = File.ReadAllText(publicRevealZonesPath);
            var loaded = JsonSerializer.Deserialize<List<PublicRevealZone>>(json);

            publicRevealZones.Clear();

            if (loaded != null)
            {
                publicRevealZones.AddRange(loaded);
            }

            Core.Log.LogInfo($"Loaded {publicRevealZones.Count} public reveal zones.");
        }
        catch (Exception e)
        {
            Core.Log.LogError($"Failed to load public reveal zones: {e}");
        }
    }

    void SavePublicRevealZones()
    {
        try
        {
            var directory = Path.GetDirectoryName(publicRevealZonesPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            CompactPublicRevealZones();

            var json = JsonSerializer.Serialize(
                publicRevealZones,
                new JsonSerializerOptions { WriteIndented = true }
            );

            File.WriteAllText(publicRevealZonesPath, json);
            Core.Log.LogInfo($"Saved {publicRevealZones.Count} public reveal zones.");
        }
        catch (Exception e)
        {
            Core.Log.LogError($"Failed to save public reveal zones: {e}");
        }
    }

    void CompactPublicRevealZones()
    {
        if (publicRevealZones.Count <= 1)
        {
            return;
        }

        var compactedZones = publicRevealZones
            .GroupBy(zone => new { zone.X, zone.Y })
            .Select(group =>
            {
                var largestRadius = group.Max(zone => zone.Radius);

                // Prefer the zone with the largest radius. If multiple tie, keep the first.
                var representative = group
                    .OrderByDescending(zone => zone.Radius)
                    .First();

                return new PublicRevealZone
                {
                    Name = representative.Name,
                    X = representative.X,
                    Y = representative.Y,
                    Radius = largestRadius
                };
            })
            .ToList();

        var removedCount = publicRevealZones.Count - compactedZones.Count;

        if (removedCount > 0)
        {
            Core.Log.LogInfo($"Compacted public reveal zones: removed {removedCount} duplicate entr{(removedCount == 1 ? "y" : "ies")}.");
        }

        publicRevealZones.Clear();
        publicRevealZones.AddRange(compactedZones);
    }

    public bool RemovePublicRevealZone(Entity characterEntity, int index, out string message)
    {
        if (publicRevealZones.Count == 0)
        {
            message = "No saved public reveal zones to remove.";
            return false;
        }

        if (index > 0)
        {
            var zeroBasedIndex = index - 1;

            if (zeroBasedIndex < 0 || zeroBasedIndex >= publicRevealZones.Count)
            {
                message = $"Invalid reveal zone index {index}. Use .waygate listrevealzones to see valid indexes.";
                return false;
            }

            var removed = publicRevealZones[zeroBasedIndex];
            publicRevealZones.RemoveAt(zeroBasedIndex);
            SavePublicRevealZones();

            message = $"Removed public reveal zone #{index}: {removed.Name} at bitmap ({removed.X}, {removed.Y}), radius {removed.Radius}.";
            return true;
        }

        if (characterEntity == Entity.Null || !Core.EntityManager.Exists(characterEntity))
        {
            message = "Could not remove closest reveal zone; character entity is invalid.";
            return false;
        }

        var pos = characterEntity.Read<Translation>().Value;
        var (bitmapX, bitmapY) = WorldPositionToRevealBitmap(pos);

        var closestIndex = -1;
        var closestDistanceSq = float.MaxValue;

        for (var i = 0; i < publicRevealZones.Count; i++)
        {
            var zone = publicRevealZones[i];

            var dx = zone.X - bitmapX;
            var dy = zone.Y - bitmapY;
            var distanceSq = (dx * dx) + (dy * dy);

            if (distanceSq < closestDistanceSq)
            {
                closestDistanceSq = distanceSq;
                closestIndex = i;
            }
        }

        if (closestIndex < 0)
        {
            message = "Could not find a closest public reveal zone.";
            return false;
        }

        var closestDistance = math.sqrt(closestDistanceSq);

        if (closestDistance > 10f)
        {
            message = $"Closest public reveal zone is {closestDistance:F1} bitmap pixels away. Move closer or provide an index.";
            return false;
        }

        var closestZone = publicRevealZones[closestIndex];
        publicRevealZones.RemoveAt(closestIndex);
        SavePublicRevealZones();

        message =
            $"Removed closest public reveal zone #{closestIndex + 1}: {closestZone.Name} " +
            $"at bitmap ({closestZone.X}, {closestZone.Y}), radius {closestZone.Radius}.";

        return true;
    }

    public bool RevealMainMapPixelsForUser(Entity userEntity, int centerX, int centerY, int radius)
    {
        if (userEntity == Entity.Null || !Core.EntityManager.Exists(userEntity))
        {
            Core.Log.LogWarning("Reveal failed; user entity is invalid.");
            return false;
        }

        if (!Core.EntityManager.HasBuffer<UserMapZoneElement>(userEntity))
        {
            Core.Log.LogWarning("Reveal failed; user has no UserMapZoneElement buffer.");
            return false;
        }

        var zones = Core.EntityManager.GetBuffer<UserMapZoneElement>(userEntity);
        const int RevealBitmapSize = 256;
        const int RevealBytesPerRow = 32;

        for (var i = 0; i < zones.Length; i++)
        {
            var zone = zones[i];

            if (zone.MapType != MapType.MainMap)
            {
                continue;
            }

            var zoneEntity = zone.UserZoneEntity.GetEntityOnServer();

            if (zoneEntity == Entity.Null || !Core.EntityManager.Exists(zoneEntity))
            {
                Core.Log.LogWarning("Reveal failed; MainMap UserZoneEntity is invalid.");
                return false;
            }

            if (!Core.EntityManager.HasBuffer<UserMapZonePackedRevealElement>(zoneEntity))
            {
                Core.Log.LogWarning("Reveal failed; MainMap zone entity has no reveal buffer.");
                return false;
            }

            var revealBuffer = Core.EntityManager.GetBuffer<UserMapZonePackedRevealElement>(zoneEntity);

            var changedBytes = new HashSet<int>();

            for (var y = centerY - radius; y <= centerY + radius; y++)
            {
                for (var x = centerX - radius; x <= centerX + radius; x++)
                {
                    if (x < 0 || x >= RevealBitmapSize || y < 0 || y >= RevealBitmapSize)
                    {
                        continue;
                    }

                    var dx = x - centerX;
                    var dy = y - centerY;

                    if ((dx * dx) + (dy * dy) > radius * radius)
                    {
                        continue;
                    }

                    var byteIndex = (y * RevealBytesPerRow) + (x / 8);
                    var bitIndex = x % 8;
                    var bitMask = (byte)(1 << bitIndex);

                    var element = revealBuffer[byteIndex];
                    var before = element.PackedPixel;

                    element.PackedPixel = (byte)(element.PackedPixel | bitMask);
                    revealBuffer[byteIndex] = element;

                    if (before != element.PackedPixel)
                    {
                        changedBytes.Add(byteIndex);
                    }
                }
            }

            Core.Log.LogInfo($"Revealed MainMap pixels around ({centerX}, {centerY}) radius {radius} for {userEntity.Read<User>().CharacterName}. Changed bytes: {changedBytes.Count}");
            return true;
        }

        Core.Log.LogWarning("Reveal failed; no MainMap zone found.");
        return false;
    }

    public void AddPublicRevealZone(int x, int y, int radius = 1, string name = "Public Reveal Zone")
    {
        var zone = new PublicRevealZone
        {
            Name = name,
            X = x,
            Y = y,
            Radius = radius
        };

        publicRevealZones.Add(zone);
        SavePublicRevealZones();
    }

    public void RevealPublicZoneForUser(Entity userEntity, PublicRevealZone zone)
    {
        RevealMainMapPixelsForUser(userEntity, zone.X, zone.Y, zone.Radius);
    }

    public void RevealAllPublicZonesForUser(Entity userEntity)
    {
        foreach (var zone in publicRevealZones)
        {
            RevealPublicZoneForUser(userEntity, zone);
        }
    }

    public (int x, int y) WorldPositionToRevealBitmap(float3 worldPos)
    {
        var bitmapX =
            (0.08424167f * worldPos.x) +
            (0.00001898f * worldPos.z) +
            242.16705f;

        var bitmapY =
            (0.00001130f * worldPos.x) +
            (0.08437769f * worldPos.z) +
            201.91725f;

        return (
            Mathf.RoundToInt(bitmapX),
            Mathf.RoundToInt(bitmapY)
        );
    }

    public bool AddRevealZoneAtClosestWaygate(Entity characterEntity, int radius = 1)
    {
        if (characterEntity == Entity.Null || !Core.EntityManager.Exists(characterEntity))
        {
            Core.Log.LogWarning("Could not add reveal zone at closest waygate; character entity is invalid.");
            return false;
        }

        var characterPos = characterEntity.Read<Translation>().Value;

        if (!Core.WaygateService.GetClosestSpawnedWaygate(
            characterPos,
            out var closestWaypoint,
            out var distance
        ))
        {
            Core.Log.LogWarning("Could not add reveal zone at closest waygate; no nearby spawned waygate found.");
            return false;
        }

        var waygatePos = closestWaypoint.Read<Translation>().Value;
        var (x, y) = WorldPositionToRevealBitmap(waygatePos);

        AddPublicRevealZone(
            x,
            y,
            radius,
            $"Reveal Zone {x},{y} for a spawned waygate"
        );

        Core.Log.LogInfo(
            $"Added reveal zone at closest waygate. " +
            $"Waygate world pos X={waygatePos.x:F4}, Y={waygatePos.y:F4}, Z={waygatePos.z:F4}; " +
            $"bitmap ({x}, {y}) radius {radius}; " +
            $"distance from player {distance:F2}."
        );

        return true;
    }

    public string GetPublicRevealZoneListText()
    {
        if (publicRevealZones.Count == 0)
        {
            return "No saved public reveal zones.";
        }

        var lines = new List<string>
    {
        $"Saved public reveal zones ({publicRevealZones.Count}):"
    };

        for (var i = 0; i < publicRevealZones.Count; i++)
        {
            var zone = publicRevealZones[i];

            lines.Add(
                $"{i + 1}. {zone.Name} " +
                $"at bitmap ({zone.X}, {zone.Y}), radius {zone.Radius}"
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
