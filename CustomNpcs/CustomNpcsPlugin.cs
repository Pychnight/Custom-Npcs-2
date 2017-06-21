﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reflection;
using CustomNpcs.Definitions;
using JetBrains.Annotations;
using Microsoft.Xna.Framework;
using Newtonsoft.Json;
using Terraria;
using TerrariaApi.Server;
using TShockAPI;
using TShockAPI.Hooks;

namespace CustomNpcs
{
    /// <summary>
    ///     Represents the custom NPCs plugin.
    /// </summary>
    [ApiVersion(2, 1)]
    [PublicAPI]
    public sealed class CustomNpcsPlugin : TerrariaPlugin
    {
        private static readonly string ConfigPath = Path.Combine("npcs", "config.json");
        private static readonly string NpcsPath = Path.Combine("npcs", "npcs.json");

        private readonly double[] _activeCustomNpcs = new double[Main.maxPlayers];
        private readonly bool[] _ignoreHits = new bool[Main.maxPlayers];
        private readonly object _luaLock = new object();
        private readonly bool[] _npcChecked = new bool[Main.maxNPCs];
        private readonly Random _random = new Random();

        private Config _config = new Config();

        /// <summary>
        ///     Initializes a new instance of the <see cref="CustomNpcsPlugin" /> class using the specified Main instance.
        /// </summary>
        /// <param name="game">The Main instance.</param>
        public CustomNpcsPlugin(Main game) : base(game)
        {
            Instance = this;
        }

        /// <summary>
        ///     Gets the custom NPCs plugin instance.
        /// </summary>
        public static CustomNpcsPlugin Instance { get; private set; }

        /// <summary>
        ///     Gets the author.
        /// </summary>
        public override string Author => "MarioE";

        /// <summary>
        ///     Gets the description.
        /// </summary>
        public override string Description => "Provides a custom NPC system.";

        /// <summary>
        ///     Gets the name.
        /// </summary>
        public override string Name => "CustomNpcs";

        /// <summary>
        ///     Gets the version.
        /// </summary>
        public override Version Version => Assembly.GetExecutingAssembly().GetName().Version;

        private CustomNpcManager CustomNpcManager => CustomNpcManager.Instance;
        private ReadOnlyCollection<CustomNpcDefinition> Definitions => CustomNpcManager.Definitions;

        /// <summary>
        ///     Initializes the plugin.
        /// </summary>
        public override void Initialize()
        {
            Directory.CreateDirectory("npcs");
            OnReload(new ReloadEventArgs(TSPlayer.Server));

            GeneralHooks.ReloadEvent += OnReload;
            ServerApi.Hooks.GameUpdate.Register(this, OnGameUpdate);
            ServerApi.Hooks.NpcAIUpdate.Register(this, OnNpcAiUpdate);
            ServerApi.Hooks.NpcKilled.Register(this, OnNpcKilled);
            ServerApi.Hooks.NpcLootDrop.Register(this, OnNpcLootDrop);
            ServerApi.Hooks.NpcSpawn.Register(this, OnNpcSpawn);
            ServerApi.Hooks.NpcStrike.Register(this, OnNpcStrike);
            ServerApi.Hooks.NpcTransform.Register(this, OnNpcTransform);

            Commands.ChatCommands.Add(new Command("customnpcs.cmaxspawns", CustomMaxSpawns, "cmaxspawns"));
            Commands.ChatCommands.Add(new Command("customnpcs.cspawnrate", CustomSpawnRate, "cspawnrate"));
            Commands.ChatCommands.Add(new Command("customnpcs.cspawnmob", CustomSpawnMob, "cspawnmob", "csm"));
        }

        /// <summary>
        ///     Disposes the plugin.
        /// </summary>
        /// <param name="disposing"><c>true</c> to dispose managed resources; otherwise, <c>false</c>.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                File.WriteAllText(ConfigPath, JsonConvert.SerializeObject(_config, Formatting.Indented));
                File.WriteAllText(NpcsPath, JsonConvert.SerializeObject(Definitions, Formatting.Indented));
                Utils.TryExecuteLua(CustomNpcManager.Dispose);

                GeneralHooks.ReloadEvent -= OnReload;
                ServerApi.Hooks.GameUpdate.Deregister(this, OnGameUpdate);
                ServerApi.Hooks.NpcAIUpdate.Deregister(this, OnNpcAiUpdate);
                ServerApi.Hooks.NpcKilled.Deregister(this, OnNpcKilled);
                ServerApi.Hooks.NpcLootDrop.Deregister(this, OnNpcLootDrop);
                ServerApi.Hooks.NpcSpawn.Deregister(this, OnNpcSpawn);
                ServerApi.Hooks.NpcStrike.Deregister(this, OnNpcStrike);
                ServerApi.Hooks.NpcTransform.Deregister(this, OnNpcTransform);
            }

            base.Dispose(disposing);
        }

        private void CustomMaxSpawns(CommandArgs args)
        {
            var parameters = args.Parameters;
            var player = args.Player;
            if (parameters.Count != 1)
            {
                player.SendErrorMessage($"Syntax: {Commands.Specifier}cmaxspawns <max-spawns>");
                return;
            }

            var inputMaxSpawns = parameters[0];
            if (!int.TryParse(inputMaxSpawns, out var maxSpawns) || maxSpawns < 1)
            {
                player.SendErrorMessage($"Invalid maximum spawns '{inputMaxSpawns}'.");
                return;
            }

            _config.MaxSpawns = maxSpawns;
            player.SendSuccessMessage($"Set maximum spawns to {maxSpawns}.");
        }

        private void CustomSpawnMob(CommandArgs args)
        {
            var parameters = args.Parameters;
            var player = args.Player;
            if (parameters.Count != 1 && parameters.Count != 2)
            {
                player.SendErrorMessage($"Syntax: {Commands.Specifier}cspawnmob <name> [amount]");
                return;
            }

            var inputName = parameters[0];
            var definition = CustomNpcManager.FindDefinition(inputName);
            if (definition == null)
            {
                player.SendErrorMessage($"Invalid custom NPC name '{inputName}'.");
                return;
            }

            var inputAmount = parameters.Count == 2 ? parameters[1] : "1";
            if (!int.TryParse(inputAmount, out var amount) || amount <= 0 || amount > 200)
            {
                player.SendErrorMessage($"Invalid amount '{inputAmount}'.");
                return;
            }

            var x = player.TileX;
            var y = player.TileY;
            for (var i = 0; i < amount; ++i)
            {
                TShock.Utils.GetRandomClearTileWithInRange(x, y, 50, 50, out var spawnX, out var spawnY);
                CustomNpcManager.Instance.SpawnCustomMob(definition, 16 * spawnX, 16 * spawnY);
            }
            player.SendSuccessMessage($"Spawned {amount} {inputName}(s).");
        }

        private void CustomSpawnRate(CommandArgs args)
        {
            var parameters = args.Parameters;
            var player = args.Player;
            if (parameters.Count != 1)
            {
                player.SendErrorMessage($"Syntax: {Commands.Specifier}cspawnrate <spawn-rate>");
                return;
            }

            var inputSpawnRate = parameters[0];
            if (!int.TryParse(inputSpawnRate, out var spawnRate) || spawnRate < 1)
            {
                player.SendErrorMessage($"Invalid spawn rate '{inputSpawnRate}'.");
                return;
            }

            _config.SpawnRate = spawnRate;
            player.SendSuccessMessage($"Set spawn rate to {spawnRate}.");
        }

        private void OnGameUpdate(EventArgs args)
        {
            foreach (var npc in Main.npc.Where(n => n != null && n.active))
            {
                var customNpc = CustomNpcManager.GetCustomNpc(npc);
                if (customNpc != null)
                {
                    if (customNpc.Definition.ShouldAggressivelyUpdate)
                    {
                        npc.netUpdate = true;
                    }
                    continue;
                }

                var npcId = npc.whoAmI;
                if (_npcChecked[npcId])
                {
                    continue;
                }
                _npcChecked[npcId] = true;


                var baseType = npc.netID;
                foreach (var definition in Definitions.Where(d => d.ReplacementTargetType == baseType))
                {
                    if (_random.NextDouble() < (definition.ReplacementChance ?? 0.0))
                    {
                        npc.SetDefaults(definition.BaseType);
                        var customNpc2 = CustomNpcManager.AttachCustomNpc(npc, definition);
                        TSPlayer.All.SendData(PacketTypes.NpcUpdate, "", npcId);
                        TSPlayer.All.SendData(PacketTypes.UpdateNPCName, "", npcId);

                        var onSpawn = customNpc2.Definition.OnSpawn;
                        Utils.TryExecuteLua(() => { onSpawn?.Call(customNpc2); });
                        break;
                    }
                }
            }

            foreach (var player in TShock.Players.Where(p => p != null && p.Active))
            {
                var maxSpawns = _config.MaxSpawns;
                var tplayer = player.TPlayer;
                var playerRectangle = new Rectangle((int)tplayer.position.X, (int)tplayer.position.Y, tplayer.width,
                    tplayer.height);

                if (!tplayer.immune)
                {
                    _ignoreHits[player.Index] = false;
                }

                // Count active custom NPCs.
                _activeCustomNpcs[player.Index] = 0;
                foreach (var npc in Main.npc.Where(n => n != null && n.active))
                {
                    var customNpc = CustomNpcManager.GetCustomNpc(npc);
                    if (customNpc == null)
                    {
                        continue;
                    }

                    var npcRectangle = new Rectangle((int)npc.position.X, (int)npc.position.Y, npc.width, npc.height);
                    if (npcRectangle.Intersects(playerRectangle) && !_ignoreHits[player.Index])
                    {
                        var onCollision = customNpc.Definition.OnCollision;
                        Utils.TryExecuteLua(() => { onCollision?.Call(customNpc, player); });
                        _ignoreHits[player.Index] = true;
                    }

                    if (!customNpc.Definition.CustomSpawn)
                    {
                        continue;
                    }

                    var activeRectangle = new Rectangle((int)(npc.position.X + npc.width / 2.0 - NPC.activeRangeX),
                        (int)(npc.position.Y + npc.height / 2.0 - NPC.activeRangeY), NPC.activeRangeX * 2,
                        NPC.activeRangeY * 2);
                    if (activeRectangle.Intersects(playerRectangle))
                    {
                        _activeCustomNpcs[player.Index] += npc.npcSlots;
                    }
                }
                if (_activeCustomNpcs[player.Index] > maxSpawns)
                {
                    continue;
                }

                var spawnRate = _config.SpawnRate;
                var succeeded = false;
                var x = -1;
                var y = -1;
                var safeRangeX = (int)(NPC.sWidth / 16.0 * 0.52);
                var safeRangeY = (int)(NPC.sHeight / 16.0 * 0.52);
                var spawnRangeX = (int)(NPC.sWidth / 16.0 * 0.7);
                var spawnRangeY = (int)(NPC.sHeight / 16.0 * 0.7);
                var minSafeX = Math.Max(0, player.TileX - safeRangeX);
                var maxSafeX = Math.Min(Main.maxTilesX, player.TileX + safeRangeX);
                var minSafeY = Math.Max(0, player.TileY - safeRangeY);
                var maxSafeY = Math.Min(Main.maxTilesY, player.TileY + safeRangeY);
                var minX = Math.Max(0, player.TileX - spawnRangeX);
                var maxX = Math.Min(Main.maxTilesX, player.TileX + spawnRangeX);
                var minY = Math.Max(0, player.TileY - spawnRangeY);
                var maxY = Math.Min(Main.maxTilesY, player.TileY + spawnRangeY);
                for (var i = 0; i < 50; ++i)
                {
                    x = _random.Next(minX, maxX);
                    y = _random.Next(minY, maxY);
                    var tile = Main.tile[x, y];
                    if (tile.nactive() && Main.tileSolid[tile.type] || Main.wallHouse[tile.wall])
                    {
                        continue;
                    }

                    // Search downwards until we hit the ground.
                    for (var y2 = y; y2 < Main.maxTilesY; ++y2)
                    {
                        var tile2 = Main.tile[x, y2];
                        if (tile2.nactive() && Main.tileSolid[tile2.type])
                        {
                            succeeded = true;
                            y = y2;
                            break;
                        }
                    }

                    // Make sure the NPC has space to spawn.
                    if (succeeded)
                    {
                        var minCheckX = Math.Max(0, x - NPC.spawnSpaceX / 2);
                        var maxCheckX = Math.Min(Main.maxTilesX, x + NPC.spawnSpaceX / 2);
                        var minCheckY = Math.Max(0, y - NPC.spawnSpaceY);
                        for (var x2 = minCheckX; x2 < maxCheckX; ++x2)
                        {
                            for (var y2 = minCheckY; y2 < y; ++y2)
                            {
                                // Don't allow the NPC to spawn within the safe range.
                                if (x2 >= minSafeX && x2 < maxSafeX || y2 >= minSafeY && y2 < maxSafeY)
                                {
                                    succeeded = false;
                                    break;
                                }

                                // Don't allow the NPC to spawn within tiles.
                                var tile2 = Main.tile[x2, y2];
                                if (tile2.nactive() && Main.tileSolid[tile2.type] || tile2.lava())
                                {
                                    succeeded = false;
                                    break;
                                }
                            }

                            if (!succeeded)
                            {
                                break;
                            }
                        }
                    }
                    if (succeeded)
                    {
                        break;
                    }
                }
                if (!succeeded)
                {
                    continue;
                }

                foreach (var definition in Definitions.Where(d => d.CustomSpawn))
                {
                    var onCheckSpawn = definition.OnCheckSpawn;
                    var breakFromLoop = false;
                    Utils.TryExecuteLua(() =>
                    {
                        if (_random.Next((int)(spawnRate * definition.CustomSpawnRateMultiplier ?? 1)) == 0 &&
                            (bool)(onCheckSpawn?.Call(player, x, y)?[0] ?? true))
                        {
                            breakFromLoop = true;
                            CustomNpcManager.SpawnCustomMob(definition, 16 * x + 8, 16 * y);
                        }
                    });

                    if (breakFromLoop)
                    {
                        break;
                    }
                }
            }
        }

        private void OnNpcAiUpdate(NpcAiUpdateEventArgs args)
        {
            if (args.Handled)
            {
                return;
            }

            var npc = args.Npc;
            var customNpc = CustomNpcManager.GetCustomNpc(npc);
            if (customNpc == null)
            {
                return;
            }

            var onAiUpdate = customNpc.Definition.OnAiUpdate;
            Utils.TryExecuteLua(() => { args.Handled = (bool)(onAiUpdate?.Call(customNpc)?[0] ?? false); });
        }

        private void OnNpcKilled(NpcKilledEventArgs args)
        {
            var npc = args.npc;
            var customNpc = CustomNpcManager.GetCustomNpc(npc);
            if (customNpc == null)
            {
                return;
            }

            var onKilled = customNpc.Definition.OnKilled;
            Utils.TryExecuteLua(() => { onKilled?.Call(customNpc); });

            var lootEntries = customNpc.Definition.LootEntries;
            if (lootEntries == null)
            {
                return;
            }

            foreach (var lootEntry in lootEntries)
            {
                if (_random.NextDouble() < lootEntry.Chance)
                {
                    var stackSize = _random.Next(lootEntry.MinStackSize, lootEntry.MaxStackSize);
                    Item.NewItem((int)npc.position.X, (int)npc.position.Y, npc.width, npc.height, lootEntry.Type,
                        stackSize, false, lootEntry.Prefix);
                }
            }

            if (!customNpc.Definition.LootOverride)
            {
                return;
            }

            double coins = npc.value;
            if (npc.midas)
            {
                coins *= 1 + _random.Next(10, 50) * 0.01;
            }
            coins *= 1 + _random.Next(-20, 21) * 0.01;
            coins = new[] {5, 10, 15, 20}
                .Where(w => _random.Next(w) == 0)
                .Aggregate(coins, (c, w) => c * (1 + _random.Next(w, 2 * w + 1) * 0.01));
            coins += npc.extraValue;

            while (coins > 0)
            {
                var coinSizes = new[] {1000000, 10000, 100, 1};
                for (var i = 0; i < coinSizes.Length && coins > 0; ++i)
                {
                    var coinSize = coinSizes[i];
                    var stack = (int)(coins / coinSize);
                    if (stack > 50 && _random.Next(5) == 0)
                    {
                        stack /= Main.rand.Next(3) + 1;
                    }
                    if (coinSize == 1)
                    {
                        if (Main.rand.Next(5) == 0)
                        {
                            stack /= Main.rand.Next(4) + 1;
                        }
                        stack = Math.Max(1, stack);
                    }
                    else if (Main.rand.Next(5) == 0)
                    {
                        stack /= Main.rand.Next(3) + 1;
                    }
                    coins -= coinSize * stack;
                    Item.NewItem((int)npc.position.X, (int)npc.position.Y, npc.width, npc.height, 74 - i, stack);
                }
            }
        }

        private void OnNpcLootDrop(NpcLootDropEventArgs args)
        {
            if (args.Handled)
            {
                return;
            }

            var npc = Main.npc[args.NpcArrayIndex];
            var customNpc = CustomNpcManager.GetCustomNpc(npc);
            if (customNpc == null)
            {
                return;
            }

            args.Handled = customNpc.Definition.LootOverride;
        }

        private void OnNpcSpawn(NpcSpawnEventArgs args)
        {
            if (args.Handled)
            {
                return;
            }

            _npcChecked[args.NpcId] = false;
        }

        private void OnNpcStrike(NpcStrikeEventArgs args)
        {
            if (args.Handled)
            {
                return;
            }

            var npc = args.Npc;
            var customNpc = CustomNpcManager.GetCustomNpc(npc);
            if (customNpc == null)
            {
                return;
            }

            if (customNpc.Definition.ShouldUpdateOnHit)
            {
                npc.netUpdate = true;
            }

            var player = TShock.Players[args.Player.whoAmI];
            var onStrike = customNpc.Definition.OnStrike;
            Utils.TryExecuteLua(() =>
            {
                if ((bool)(onStrike?.Call(customNpc, player, args.Damage, args.KnockBack, args.Critical)?[0] ??
                           false))
                {
                    args.Handled = true;
                }
            });
        }

        private void OnNpcTransform(NpcTransformationEventArgs args)
        {
            if (args.Handled)
            {
                return;
            }

            _npcChecked[args.NpcId] = false;
        }

        private void OnReload(ReloadEventArgs args)
        {
            Utils.TryExecuteLua(CustomNpcManager.Dispose);
            if (File.Exists(ConfigPath))
            {
                _config = JsonConvert.DeserializeObject<Config>(File.ReadAllText(ConfigPath));
            }
            if (File.Exists(NpcsPath))
            {
                var definitions = JsonConvert.DeserializeObject<List<CustomNpcDefinition>>(File.ReadAllText(NpcsPath));
                foreach (var definition in definitions)
                {
                    CustomNpcManager.AddDefinition(definition);
                    Utils.TryExecuteLua(definition.LoadLuaDefinition);
                }
            }
        }
    }
}
