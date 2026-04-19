using System;
using System.Text.Json;
using Godot;

namespace SpacedOut.Meta;

/// <summary>
/// Loads, saves and mutates the persistent <see cref="MetaProfile"/> backed by
/// <c>user://profile.json</c>. All mutations call <see cref="Save"/> immediately
/// so that a crash or quit never loses progression.
/// </summary>
public class MetaProgressService
{
    private const string ProfilePath = "user://profile.json";

    public MetaProfile Profile { get; private set; } = new();

    private static readonly JsonSerializerOptions _serializerOpts = new()
    {
        WriteIndented = true,
    };

    /// <summary>Loads the profile from disk; if the file does not exist or fails to parse, a fresh profile is kept.</summary>
    public void Load()
    {
        if (!FileAccess.FileExists(ProfilePath))
        {
            Profile = new MetaProfile();
            Save();
            GD.Print($"[Meta] Neues Profil angelegt: {ProfilePath}");
            return;
        }

        try
        {
            using var f = FileAccess.Open(ProfilePath, FileAccess.ModeFlags.Read);
            string json = f.GetAsText();
            var loaded = JsonSerializer.Deserialize<MetaProfile>(json);
            Profile = loaded ?? new MetaProfile();
            GD.Print($"[Meta] Profil geladen — Sternenstaub={Profile.Stardust}, Unlocks={Profile.UnlockedIds.Count}, Runs={Profile.RunsCompleted}");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[Meta] Profil konnte nicht geladen werden ({ex.Message}) — frisches Profil wird genutzt.");
            Profile = new MetaProfile();
        }
    }

    public void Save()
    {
        try
        {
            string json = JsonSerializer.Serialize(Profile, _serializerOpts);
            using var f = FileAccess.Open(ProfilePath, FileAccess.ModeFlags.Write);
            if (f == null)
            {
                GD.PrintErr($"[Meta] Profil-Datei konnte nicht zum Schreiben geöffnet werden: {ProfilePath}");
                return;
            }
            f.StoreString(json);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[Meta] Profil-Speichern fehlgeschlagen: {ex.Message}");
        }
    }

    /// <summary>Adds Sternenstaub (negative values are clamped) and persists immediately.</summary>
    public void GrantStardust(int amount)
    {
        if (amount == 0) return;
        Profile.Stardust = Math.Max(0, Profile.Stardust + amount);
        Save();
    }

    /// <summary>True when the unlock has been purchased.</summary>
    public bool HasUnlock(string id) => Profile.UnlockedIds.Contains(id);

    /// <summary>
    /// Buys an unlock at its catalog cost. Returns false if id is unknown,
    /// already owned or affordable Stardust is missing. On success the cost is deducted
    /// and the profile is persisted.
    /// </summary>
    public bool Purchase(string id)
    {
        var def = UnlockCatalog.GetById(id);
        if (def == null) return false;
        if (Profile.UnlockedIds.Contains(id)) return false;
        if (Profile.Stardust < def.Cost) return false;

        Profile.Stardust -= def.Cost;
        Profile.UnlockedIds.Add(id);
        Save();
        GD.Print($"[Meta] Unlock gekauft: {id} (-{def.Cost} Sternenstaub, Rest {Profile.Stardust})");
        return true;
    }

    /// <summary>Sets the active loadout perk (null = none) and persists.</summary>
    public void SetSelectedPerk(string? id)
    {
        Profile.SelectedPerkId = id;
        Save();
    }

    /// <summary>Wipes the profile back to defaults and persists. Used by debug tooling.</summary>
    public void ResetProfile()
    {
        Profile = new MetaProfile();
        Save();
        GD.Print("[Meta] Profil zurückgesetzt.");
    }
}
