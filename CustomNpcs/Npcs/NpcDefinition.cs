﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Terraria;
using CustomNpcs.Projectiles;
using System.Reflection;
using TShockAPI;
using System.Diagnostics;
using BooTS;
using Corruption.PluginSupport;

namespace CustomNpcs.Npcs
{
    /// <summary>
    ///     Represents an NPC definition.
    /// </summary>
    [JsonObject(MemberSerialization.OptIn)]
    public sealed class NpcDefinition : DefinitionBase, IDisposable
    {
		//internal string originalName; //we need to capture the npc's original name before applying our custom name to it, so the exposed lua function
		//NameContains() works...

		/// <summary>
		///     Gets the internal name.
		/// </summary>
		[JsonProperty(Order = 0)]
		public override string Name { get; protected internal set; } = "example";

		/// <summary>
		///     Gets the base type.
		/// </summary>
		[JsonProperty(Order = 1)]
		public int BaseType { get; private set; }

		[JsonProperty(Order = 2)]
		public override string ScriptPath { get; protected internal set; }
		
		[JsonProperty("BaseOverride", Order = 3)]
        internal BaseOverrideDefinition _baseOverride = new BaseOverrideDefinition();

        [JsonProperty("Loot", Order = 4)]
        private LootDefinition _loot = new LootDefinition();

        [JsonProperty("Spawning", Order = 5)]
        private SpawningDefinition _spawning = new SpawningDefinition();
		
        /// <summary>
        ///     Gets the loot entries.
        /// </summary>
        public List<LootEntryDefinition> LootEntries => _loot.Entries;

		/// <summary>
		///     Gets a function that is invoked when the NPC is checked for replacing.
		/// </summary>
		public NpcCheckReplaceHandler OnCheckReplace { get; internal set; }

		/// <summary>
		///     Gets a function that is invoked when the NPC is checked for spawning.
		/// </summary>
		public NpcCheckSpawnHandler OnCheckSpawn { get; internal set; }

		/// <summary>
		///     Gets a function that is invoked when the NPC is spawned.
		/// </summary>
		public NpcSpawnHandler OnSpawn { get; internal set; }

		/// <summary>
		///     Gets a function that is invoked when the NPC collides with a player.
		/// </summary>
		public NpcCollisionHandler OnCollision { get; internal set; }

		/// <summary>
		///     Gets a function that is invoked when the NPC collides with a tile.
		/// </summary>
		public NpcTileCollisionHandler OnTileCollision { get; internal set; }

		/// <summary>
		///     Gets a function that is invoked when NPC is killed.
		/// </summary>
		public NpcKilledHandler OnKilled { get; internal set; }

		/// <summary>
		///     Gets a function that is invoked after the NPC has transformed.
		/// </summary>
		public NpcTransformedHandler OnTransformed { get; internal set; }

		/// <summary>
		///     Gets a function that is invoked when the NPC is struck.
		/// </summary>
		public NpcStrikeHandler OnStrike { get; internal set; }

		/// <summary>
		///     Gets a function that is invoked when the NPC AI is updated.
		/// </summary>
		public NpcAiUpdateHandler OnAiUpdate { get; internal set; }		

		/// <summary>
		///     Gets a value indicating whether the NPC should aggressively update due to unsynced changes with clients.
		/// </summary>
		public bool ShouldAggressivelyUpdate =>
            _baseOverride.AiStyle != null || _baseOverride.BuffImmunities != null ||
            _baseOverride.IsImmuneToLava != null || _baseOverride.HasNoCollision != null ||
            _baseOverride.HasNoGravity != null;

        /// <summary>
        ///     Gets a value indicating whether loot should be overriden.
        /// </summary>
        public bool ShouldOverrideLoot => _loot.IsOverride;

        /// <summary>
        ///     Gets a value indicating whether the NPC should spawn.
        /// </summary>
        public bool ShouldReplace => _spawning.ShouldReplace;

        /// <summary>
        ///     Gets a value indicating whether the NPC should spawn.
        /// </summary>
        public bool ShouldSpawn => _spawning.ShouldSpawn;

		/// <summary>
		///     Gets an optional value that overrides the global spawnrate, if present.
		/// </summary>
		public int? SpawnRateOverride => _spawning.SpawnRateOverride;

		/// <summary>
		///     Gets a value indicating whether the NPC should have kills tallied.
		/// </summary>
		public bool ShouldTallyKills => _loot.TallyKills;

        /// <summary>
        ///     Gets a value indicating whether the NPC should update on hit.
        /// </summary>
        public bool ShouldUpdateOnHit =>
            _baseOverride.Defense != null || _baseOverride.IsImmortal != null ||
            _baseOverride.KnockbackMultiplier != null;

        /// <summary>
        ///     Disposes the definition.
        /// </summary>
        public void Dispose()
        {
   			OnCheckReplace = null;
			OnCheckSpawn = null;
			OnSpawn = null;
			OnKilled = null;
			OnTransformed = null;
			OnCollision = null;
			OnTileCollision = null;
			OnStrike = null;
			OnAiUpdate = null;
		}

        /// <summary>
        ///     Applies the definition to the specified NPC.
        /// </summary>
        /// <param name="npc">The NPC, which must not be <c>null</c>.</param>
        /// <exception cref="ArgumentNullException"><paramref name="npc" /> is <c>null</c>.</exception>
        public void ApplyTo(NPC npc)
        {
            if (npc == null)
            {
                throw new ArgumentNullException(nameof(npc));
            }

			// Set NPC to use four life bytes.
			Main.npcLifeBytes[BaseType] = 4;

            if (npc.netID != BaseType)
            {
                npc.SetDefaults(BaseType);
            }

            npc.aiStyle = _baseOverride.AiStyle ?? npc.aiStyle;
            if (_baseOverride.BuffImmunities != null)
            {
                for (var i = 0; i < Main.maxBuffTypes; ++i)
                {
                    npc.buffImmune[i] = false;
                }
                foreach (var i in _baseOverride.BuffImmunities)
                {
                    npc.buffImmune[i] = true;
                }
            }

            npc.defense = npc.defDefense = _baseOverride.Defense ?? npc.defense;
            npc.noGravity = _baseOverride.HasNoGravity ?? npc.noGravity;
            npc.noTileCollide = _baseOverride.HasNoCollision ?? npc.noTileCollide;
			npc.behindTiles = _baseOverride.BehindTiles ?? npc.behindTiles;
			npc.boss = _baseOverride.IsBoss ?? npc.boss;
            npc.immortal = _baseOverride.IsImmortal ?? npc.immortal;
            npc.lavaImmune = _baseOverride.IsImmuneToLava ?? npc.lavaImmune;
            npc.trapImmune = _baseOverride.IsTrapImmune ?? npc.trapImmune;
			npc.dontTakeDamageFromHostiles = _baseOverride.DontTakeDamageFromHostiles ?? npc.dontTakeDamageFromHostiles;
			npc.knockBackResist = _baseOverride.KnockbackMultiplier ?? npc.knockBackResist;
			// Don't set npc.lifeMax so that the correct life is always sent to clients.
            npc.life = _baseOverride.MaxHp ?? npc.life;
            npc._givenName = _baseOverride.Name ?? npc._givenName;
            npc.npcSlots = _baseOverride.NpcSlots ?? npc.npcSlots;
            npc.value = _baseOverride.Value ?? npc.value;
									
			//the following are not settable
			//npc.HasGivenName
			//npc.HasValidTarget
			//npc.HasPlayerTarget
			//npc.HasNPCTarget
		}
		
		protected override bool OnLinkToScriptAssembly(Assembly ass)
		{
			if( ass == null )
				return false;

			if( string.IsNullOrWhiteSpace(ScriptPath) )
				return false;

			var linker = new BooModuleLinker(ass, ScriptPath);

			OnCheckReplace = linker.TryCreateDelegate<NpcCheckReplaceHandler>("OnCheckReplace");
			OnCheckSpawn = linker.TryCreateDelegate<NpcCheckSpawnHandler>("OnCheckSpawn");
			OnSpawn = linker.TryCreateDelegate<NpcSpawnHandler>("OnSpawn");
			OnCollision = linker.TryCreateDelegate<NpcCollisionHandler>("OnCollision");
			OnTileCollision = linker.TryCreateDelegate<NpcTileCollisionHandler>("OnTileCollision");
			OnTransformed = linker.TryCreateDelegate<NpcTransformedHandler>("OnTransformed");
			OnKilled = linker.TryCreateDelegate<NpcKilledHandler>("OnKilled");
			OnStrike = linker.TryCreateDelegate<NpcStrikeHandler>("OnStrike");
			OnAiUpdate = linker.TryCreateDelegate<NpcAiUpdateHandler>("OnAiUpdate");
			
			return true;
		}

		//protected internal override void ThrowIfInvalid()
  //      {
  //          if (Name == null)
  //          {
  //              throw new FormatException($"{nameof(Name)} is null.");
  //          }
  //          if (int.TryParse(Name, out _))
  //          {
  //              throw new FormatException($"{nameof(Name)} cannot be a number.");
  //          }
  //          if (string.IsNullOrWhiteSpace(Name))
  //          {
  //              throw new FormatException($"{nameof(Name)} is whitespace.");
  //          }
  //          if (BaseType < -65)
  //          {
  //              throw new FormatException($"{nameof(BaseType)} is too small.");
  //          }
  //          if (BaseType >= Main.maxNPCTypes)
  //          {
  //              throw new FormatException($"{nameof(BaseType)} is too large.");
  //          }
  //          if (ScriptPath != null && !File.Exists(Path.Combine("npcs", ScriptPath)))
  //          {
  //              throw new FormatException($"{nameof(ScriptPath)} points to an invalid script file.");
  //          }
  //          if (_loot == null)
  //          {
  //              throw new FormatException("Loot is null.");
  //          }
  //          _loot.ThrowIfInvalid();
  //          if (_spawning == null)
  //          {
  //              throw new FormatException("Spawning is null.");
  //          }
  //          if (_baseOverride == null)
  //          {
  //              throw new FormatException("BaseOverride is null.");
  //          }
  //          _baseOverride.ThrowIfInvalid();
  //      }

		protected override void OnValidate(ValidationResult result)
		{
			if( Name == null )
			{
				//throw new FormatException($"{nameof(Name)} is null.");
				result.AddError($"{nameof(Name)} is null.", FilePath);
			}
			if( int.TryParse(Name, out _) )
			{
				//throw new FormatException($"{nameof(Name)} cannot be a number.");
				result.AddError($"{nameof(Name)} cannot be a number.", FilePath);
			}
			if( string.IsNullOrWhiteSpace(Name) )
			{
				//throw new FormatException($"{nameof(Name)} is whitespace.");
				result.AddError($"{nameof(Name)} is whitespace.", FilePath);
			}
			if( BaseType < -65 )
			{
				//throw new FormatException($"{nameof(BaseType)} is too small.");
				result.AddError($"{nameof(BaseType)} is too small.", FilePath);
			}
			if( BaseType >= Main.maxNPCTypes )
			{
				//throw new FormatException($"{nameof(BaseType)} is too large.");
				result.AddError($"{nameof(BaseType)} is too large.", FilePath);
			}
			if( ScriptPath != null && !File.Exists(Path.Combine("npcs", ScriptPath)) )
			{
				//throw new FormatException($"{nameof(ScriptPath)} points to an invalid script file.");
				result.AddError($"{nameof(ScriptPath)} points to an invalid script file.", FilePath);
			}
			if( _loot == null )
			{
				//throw new FormatException("Loot is null.");
				result.AddError("Loot is null.", FilePath);
			}
			//_loot.ThrowIfInvalid();
			var lootResult = _loot.Validate();
			lootResult.SetSources(FilePath);
			result.AddValidationResult(lootResult);

			if( _spawning == null )
			{
				//throw new FormatException("Spawning is null.");
				result.AddError("Spawning is null.", FilePath);
			}
			if( _baseOverride == null )
			{
				//throw new FormatException("BaseOverride is null.");
				result.AddError("BaseOverride is null.", FilePath);
			}
			//_baseOverride.ThrowIfInvalid();
			var baseResult = _baseOverride.Validate();
			baseResult.SetSources(FilePath);
			result.AddValidationResult(baseResult);
		}

		[JsonObject(MemberSerialization.OptIn)]
        internal sealed class BaseOverrideDefinition : IValidator
        {
            [JsonProperty]
            public int? AiStyle { get; private set; }

            [JsonProperty]
            public int[] BuffImmunities { get; private set; }

            [JsonProperty]
            public int? Defense { get; private set; }

            [JsonProperty]
            public bool? HasNoCollision { get; private set; }

            [JsonProperty]
            public bool? HasNoGravity { get; private set; }

            [JsonProperty]
            public bool? IsBoss { get; private set; }

            [JsonProperty]
            public bool? IsImmortal { get; private set; }

            [JsonProperty]
            public bool? IsImmuneToLava { get; private set; }

            [JsonProperty]
            public bool? IsTrapImmune { get; private set; }

            [JsonProperty]
            public float? KnockbackMultiplier { get; private set; }

            [JsonProperty]
            public int? MaxHp { get; private set; }

            [JsonProperty]
            public string Name { get; private set; }

            [JsonProperty]
            public float? NpcSlots { get; private set; }

            [JsonProperty]
            public float? Value { get; private set; }

			[JsonProperty]
			public bool? BehindTiles { get; private set; }

			[JsonProperty]
			public bool? DontTakeDamageFromHostiles { get; private set; }

			//[Obsolete]
			//         internal void ThrowIfInvalid()
			//         {
			//             if (BuffImmunities != null && BuffImmunities.Any(i => i <= 0 || i >= Main.maxBuffTypes))
			//             {
			//                 throw new FormatException($"{nameof(BuffImmunities)} must contain valid buff types.");
			//             }
			//             if (KnockbackMultiplier < 0)
			//             {
			//                 throw new FormatException($"{nameof(KnockbackMultiplier)} must be non-negative.");
			//             }
			//             if (MaxHp < 0)
			//             {
			//                 throw new FormatException($"{nameof(MaxHp)} must be non-negative.");
			//             }
			//             if (Value < 0)
			//             {
			//                 throw new FormatException($"{nameof(Value)} must be non-negative.");
			//             }
			//         }

			public ValidationResult Validate()
			{
				var result = new ValidationResult();

				if( BuffImmunities != null && BuffImmunities.Any(i => i <= 0 || i >= Main.maxBuffTypes) )
				{
					//throw new FormatException($"{nameof(BuffImmunities)} must contain valid buff types.");
					result.AddError($"{nameof(BuffImmunities)} must contain valid buff types.");
				}
				if( KnockbackMultiplier < 0 )
				{
					//throw new FormatException($"{nameof(KnockbackMultiplier)} must be non-negative.");
					result.AddError($"{nameof(KnockbackMultiplier)} must be non-negative.");
				}
				if( MaxHp < 0 )
				{
					//throw new FormatException($"{nameof(MaxHp)} must be non-negative.");
					result.AddError($"{nameof(MaxHp)} must be non-negative.");
				}
				if( Value < 0 )
				{
					//throw new FormatException($"{nameof(Value)} must be non-negative.");
					result.AddError($"{nameof(Value)} must be non-negative.");
				}

				return result;
			}
		}

        [JsonObject(MemberSerialization.OptIn)]
        internal sealed class LootDefinition : IValidator
        {
            [JsonProperty(Order = 2)]
            public List<LootEntryDefinition> Entries { get; private set; } = new List<LootEntryDefinition>();

            [JsonProperty(Order = 1)]
            public bool IsOverride { get; private set; }

            [JsonProperty(Order = 0)]
            public bool TallyKills { get; private set; }

			public ValidationResult Validate()
			{
				var result = new ValidationResult();

				if( Entries == null )
				{
					throw new FormatException($"{nameof(Entries)} is null.");
				}
				foreach( var entry in Entries )
				{
					//entry.ThrowIfInvalid();
					var res = entry.Validate();
					result.AddValidationResult(res);

				}

				return result;
			}
		}

        [JsonObject(MemberSerialization.OptIn)]
        internal sealed class SpawningDefinition
        {
            [JsonProperty(Order = 1)]
            public bool ShouldReplace { get; private set; }

            [JsonProperty(Order = 0)]
            public bool ShouldSpawn { get; private set; }

			[JsonProperty(Order = 2)]
			public int? SpawnRateOverride { get; private set; }
		}
    }
}
