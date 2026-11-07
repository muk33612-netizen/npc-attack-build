using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Forms;
using GTA;
using GTA.Math;
using GTA.Native;

// NpcAttack: nearby pedestrians pull weapons and fight the player.
// Single-player only. Needs ScriptHookVDotNet 3.
public class NpcAttack : Script
{
    // ---- settings (overridden by NpcAttack.ini) ----
    Keys toggleKey = Keys.F9;
    Keys waveKey = Keys.F10;
    int radius = 60;
    int maxAttackers = 12;
    int accuracy = 25;
    int waveSize = 5;
    bool skipCops = true;
    bool includeDrivers = false;
    readonly List<WeaponHash> weapons = new List<WeaponHash>();

    // ---- state ----
    const int HardCap = 30;
    bool active;
    int nextScan;
    RelationshipGroup hostile;
    readonly Dictionary<int, Ped> attackers = new Dictionary<int, Ped>();
    readonly Dictionary<int, RelationshipGroup> original = new Dictionary<int, RelationshipGroup>();
    readonly Random rng = new Random();

    static readonly string[] gangModels =
    {
        "g_m_y_ballaeast_01", "g_m_y_famca_01", "g_m_y_lost_01",
        "g_m_y_mexgoon_01", "g_m_y_salvagoon_01", "g_m_y_korean_01"
    };

    public NpcAttack()
    {
        LoadSettings();
        hostile = World.AddRelationshipGroup("NPCATK_HOSTILE");

        Interval = 100;
        Tick += OnTick;
        KeyDown += OnKeyDown;
        Aborted += (s, e) => ReleaseAll();
    }

    void LoadSettings()
    {
        string names = "Pistol,CombatPistol,MicroSMG,PumpShotgun,CarbineRifle,Bat";
        try
        {
            ScriptSettings ini = ScriptSettings.Load(Path.Combine("scripts", "NpcAttack.ini"));
            toggleKey = ini.GetValue("Keys", "Toggle", Keys.F9);
            waveKey = ini.GetValue("Keys", "Wave", Keys.F10);
            radius = ini.GetValue("Settings", "Radius", 60);
            maxAttackers = ini.GetValue("Settings", "MaxAttackers", 12);
            accuracy = ini.GetValue("Settings", "Accuracy", 25);
            waveSize = ini.GetValue("Settings", "WaveSize", 5);
            skipCops = ini.GetValue("Settings", "SkipCops", true);
            includeDrivers = ini.GetValue("Settings", "IncludeDrivers", false);
            names = ini.GetValue("Settings", "Weapons", names);
        }
        catch (Exception)
        {
            // keep defaults
        }

        foreach (string n in names.Split(','))
        {
            WeaponHash w;
            if (Enum.TryParse(n.Trim(), true, out w))
                weapons.Add(w);
        }
        if (weapons.Count == 0)
            weapons.Add(WeaponHash.Pistol);
    }

    void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.KeyCode == toggleKey)
        {
            active = !active;
            if (active)
            {
                Ped player = Game.Player.Character;
                hostile.SetRelationshipBetweenGroups(player.RelationshipGroup, Relationship.Hate, true);
                hostile.SetRelationshipBetweenGroups(hostile, Relationship.Companion, true);
            }
            else
            {
                ReleaseAll();
            }
            GTA.UI.Screen.ShowSubtitle(active ? "NPC Attack: ON" : "NPC Attack: OFF", 1500);
        }
        else if (e.KeyCode == waveKey)
        {
            SpawnWave();
        }
    }

    void OnTick(object sender, EventArgs e)
    {
        if (attackers.Count == 0 && !active) return;

        Ped player = Game.Player.Character;
        if (player == null || !player.Exists() || player.IsDead) return;
        if (Game.GameTime < nextScan) return;
        nextScan = Game.GameTime + 500;

        // forget attackers that died or despawned
        List<int> gone = new List<int>();
        foreach (KeyValuePair<int, Ped> kv in attackers)
        {
            if (kv.Value == null || !kv.Value.Exists() || kv.Value.IsDead)
                gone.Add(kv.Key);
        }
        foreach (int h in gone)
        {
            attackers.Remove(h);
            original.Remove(h);
        }

        // recruit nearby pedestrians
        if (active && attackers.Count < maxAttackers)
        {
            foreach (Ped ped in World.GetNearbyPeds(player, radius))
            {
                if (attackers.Count >= maxAttackers) break;
                if (CanRecruit(ped)) Arm(ped, player);
            }
        }

        // keep everyone on the player
        foreach (Ped ped in attackers.Values)
        {
            if (!ped.IsInCombatAgainst(player))
                ped.Task.FightAgainst(player);
        }
    }

    bool CanRecruit(Ped ped)
    {
        if (ped == null || !ped.Exists() || ped.IsDead) return false;
        if (ped.IsPlayer || !ped.IsHuman) return false;
        if (ped.IsPersistent) return false; // mission peds
        if (attackers.ContainsKey(ped.Handle)) return false;
        if (!includeDrivers && ped.IsInVehicle()) return false;
        if (skipCops && (ped.PedType == PedType.Cop || ped.PedType == PedType.Swat)) return false;
        return true;
    }

    void Arm(Ped ped, Ped player)
    {
        original[ped.Handle] = ped.RelationshipGroup;
        ped.RelationshipGroup = hostile;

        WeaponHash w = weapons[rng.Next(weapons.Count)];
        ped.Weapons.Give(w, 250, true, true);

        ped.Accuracy = accuracy;
        ped.CanSwitchWeapons = true;
        ped.AlwaysKeepTask = true;
        ped.BlockPermanentEvents = true;

        Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, ped.Handle, 46, true); // always fight
        Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, ped.Handle, 5, true);  // fight even when unarmed target
        Function.Call(Hash.SET_PED_FLEE_ATTRIBUTES, ped.Handle, 0, false);
        Function.Call(Hash.SET_PED_COMBAT_ABILITY, ped.Handle, 1);           // average
        Function.Call(Hash.SET_PED_COMBAT_MOVEMENT, ped.Handle, 2);          // will advance

        ped.Task.FightAgainst(player);
        attackers[ped.Handle] = ped;
    }

    void SpawnWave()
    {
        Ped player = Game.Player.Character;
        if (player == null || !player.Exists()) return;

        hostile.SetRelationshipBetweenGroups(player.RelationshipGroup, Relationship.Hate, true);
        hostile.SetRelationshipBetweenGroups(hostile, Relationship.Companion, true);

        int spawned = 0;
        for (int i = 0; i < waveSize && attackers.Count < HardCap; i++)
        {
            Model model = new Model(gangModels[rng.Next(gangModels.Length)]);
            if (!model.Request(1000)) continue;

            Vector3 pos = player.Position.Around(rng.Next(15, 30));
            Vector3 ground = World.GetNextPositionOnSidewalk(pos);
            if (ground != Vector3.Zero) pos = ground;

            Ped ped = World.CreatePed(model, pos);
            model.MarkAsNoLongerNeeded();
            if (ped == null || !ped.Exists()) continue;

            Arm(ped, player);
            spawned++;
        }
        GTA.UI.Screen.ShowSubtitle("Wave: " + spawned, 1500);
    }

    void ReleaseAll()
    {
        foreach (KeyValuePair<int, Ped> kv in attackers)
        {
            Ped p = kv.Value;
            if (p == null || !p.Exists()) continue;

            RelationshipGroup g;
            if (original.TryGetValue(kv.Key, out g))
                p.RelationshipGroup = g;

            p.BlockPermanentEvents = false;
            p.AlwaysKeepTask = false;
            p.Task.ClearAll();
            p.MarkAsNoLongerNeeded();
        }
        attackers.Clear();
        original.Clear();
    }
}
