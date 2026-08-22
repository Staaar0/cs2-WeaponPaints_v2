using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Entities;
using CounterStrikeSharp.API.Modules.Memory;
using CounterStrikeSharp.API.Modules.Memory.DynamicFunctions;
using Microsoft.Extensions.Logging;

namespace WeaponPaints
{
	public partial class WeaponPaints
	{
		private bool _mvpPlayed;
		private bool _stopping;
		private int _worldGeneration;
		
		[GameEventHandler]
		public HookResult OnClientFullConnect(EventPlayerConnectFull @event, GameEventInfo info)
     	{
			CCSPlayerController? player = @event.Userid;

			if (player is null || !player.IsValid || player.IsBot ||
				WeaponSync == null || Database == null) return HookResult.Continue;

			var playerInfo = new PlayerInfo
			{
				UserId = player.UserId,
				Slot = player.Slot,
				Index = (int)player.Index,
				SteamId = player.SteamID.ToString(),
				Name = player.PlayerName,
				IpAddress = player.IpAddress?.Split(":")[0]
			};

			try
			{
				_ = Task.Run(async () => await WeaponSync.GetPlayerData(playerInfo));
				/*
				if (Config.Additional.SkinEnabled)
				{
					_ = Task.Run(async () => await weaponSync.GetWeaponPaintsFromDatabase(playerInfo));
				}
				if (Config.Additional.KnifeEnabled)
				{
					_ = Task.Run(async () => await weaponSync.GetKnifeFromDatabase(playerInfo));
				}
				if (Config.Additional.GloveEnabled)
				{
					_ = Task.Run(async () => await weaponSync.GetGloveFromDatabase(playerInfo));
				}
				if (Config.Additional.AgentEnabled)
				{
					_ = Task.Run(async () => await weaponSync.GetAgentFromDatabase(playerInfo));
				}
				if (Config.Additional.MusicEnabled)
				{
					_ = Task.Run(async () => await weaponSync.GetMusicFromDatabase(playerInfo));
				}
				*/
			}
			catch
			{
			}
			
			Players.Add(player);

			return HookResult.Continue;
		}

		[GameEventHandler]
		public HookResult OnPlayerDisconnect(EventPlayerDisconnect @event, GameEventInfo info)
		{
			CCSPlayerController? player = @event.Userid;

			if (player is null || !player.IsValid || player.IsBot) return HookResult.Continue;

			var playerInfo = new PlayerInfo
			{
				UserId = player.UserId,
				Slot = player.Slot,
				Index = (int)player.Index,
				SteamId = player.SteamID.ToString(),
				Name = player.PlayerName,
				IpAddress = player.IpAddress?.Split(":")[0]
			};

			Task.Run(async () => 
			{
				if (WeaponSync != null)
					await WeaponSync.SyncStatTrakToDatabase(playerInfo);

				if (Config.Additional.SkinEnabled)
				{
					GPlayerWeaponsInfo.TryRemove(player.Slot, out _);
				}
			});

			if (Config.Additional.KnifeEnabled)
			{
				GPlayersKnife.TryRemove(player.Slot, out _);
			}
			if (Config.Additional.GloveEnabled)
			{
				GPlayersGlove.TryRemove(player.Slot, out _);
				GPlayersNativeGlove.TryRemove(player.Slot, out _);
			}
			if (Config.Additional.AgentEnabled)
			{
				GPlayersAgent.TryRemove(player.Slot, out _);
			}
			if (Config.Additional.MusicEnabled)
			{
				GPlayersMusic.TryRemove(player.Slot, out _);
			}
			if (Config.Additional.PinsEnabled)
			{
				GPlayersPin.TryRemove(player.Slot, out _);
				GPlayersNativePin.TryRemove(player.Slot, out _);
			}
			
			_temporaryPlayerWeaponWear.TryRemove(player.Slot, out _);
			GPlayersStickerSearch.TryRemove(player.Slot, out _);
			GPlayersPendingStickerSearch.TryRemove(player.Slot, out _);
			GPlayerWeaponPaintCustomizations.TryRemove(player.Slot, out _);
			GPlayersPendingSeedWearInput.TryRemove(player.Slot, out _);
			CommandsCooldown.Remove(player.Slot);
			Players.Remove(player);

			return HookResult.Continue;
		}

		private void OnMapStart(string mapName)
		{
			_worldGeneration++;

			if (Config.Additional is { KnifeEnabled: false, SkinEnabled: false, GloveEnabled: false }) return;
			
			if (Database != null)
				WeaponSync = new WeaponSynchronization(Database, Config);

			// keep the fade seed above 0, fade skins render black with seed 0
			_fadeSeed = 1;
			_nextItemId = MinimumCustomItemId;
		}

		private HookResult OnPlayerSpawn(EventPlayerSpawn @event, GameEventInfo info)
		{
			CCSPlayerController? player = @event.Userid;

			if (player is null || !player.IsValid || Config.Additional is { KnifeEnabled: false, GloveEnabled: false })
				return HookResult.Continue;

			CCSPlayerPawn? pawn = player.PlayerPawn.Value;

			if (pawn == null || !pawn.IsValid)
				return HookResult.Continue;

			GivePlayerMusicKit(player);
			GivePlayerAgent(player);
			Server.NextFrame(() =>
			{
				GivePlayerGloves(player, true, false);
			});
			GivePlayerPin(player);

			return HookResult.Continue;
		}

		private HookResult OnRoundEnd(EventRoundEnd @event, GameEventInfo info)
		{
			_gBCommandsAllowed = false;
			return HookResult.Continue;
		}

		private HookResult OnRoundStart(EventRoundStart @event, GameEventInfo info)
		{
			_gBCommandsAllowed = true;
			_mvpPlayed = false;
			return HookResult.Continue;
		}
		
		private HookResult OnRoundMvp(EventRoundMvp @event, GameEventInfo info)
		{
			if (_mvpPlayed)
				return HookResult.Continue;
			
			var player = @event.Userid;
			
			if (player == null || !player.IsValid || player.IsBot)
				return HookResult.Continue;

			if (!(GPlayersMusic.TryGetValue(player.Slot, out var musicInfo)
			      && musicInfo.TryGetValue(player.Team, out var musicId)
			      && musicId != 0))
				return HookResult.Continue;
					
			@event.Musickitid = musicId;
			@event.Nomusic = 0;
			info.DontBroadcast = true;
			
			var newEvent = new EventRoundMvp(true)
			{
				Userid = player,
				Musickitid = musicId,
				Nomusic = 0,
			};

			_mvpPlayed = true;
			
			newEvent.FireEvent(false);
			return HookResult.Continue;
		}

		private HookResult OnGiveNamedItemPost(DynamicHook hook)
		{
			try
			{
				var itemServices = hook.GetParam<CCSPlayer_ItemServices>(0);
				var weapon = hook.GetReturn<CBasePlayerWeapon>();
				if (weapon == null || !weapon.IsValid || !weapon.DesignerName.Contains("weapon"))
					return HookResult.Continue;

				var player = GetPlayerFromItemServices(itemServices);
				if (player == null)
					return HookResult.Continue;

				var weaponHandle = weapon.EntityHandle.Raw;
				var playerSlot = player.Slot;
				var steamId = player.SteamID;
				var generation = _worldGeneration;

				Server.NextWorldUpdate(() =>
				{
					if (_stopping || generation != _worldGeneration)
						return;

					try
					{
						var weaponPointer = EntitySystem.GetEntityByHandle(weaponHandle);
						if (weaponPointer is null || weaponPointer == IntPtr.Zero)
							return;

						var currentWeapon = new CBasePlayerWeapon(weaponPointer.Value);
						if (!currentWeapon.IsValid || !currentWeapon.DesignerName.Contains("weapon"))
							return;

						var currentPlayer = Utilities.GetPlayerFromSlot(playerSlot);
						if (!Utility.IsPlayerValid(currentPlayer) || currentPlayer!.SteamID != steamId)
							return;

						GivePlayerWeaponSkin(currentPlayer, currentWeapon);
					}
					catch (Exception ex)
					{
						Logger.LogWarning(ex, "Failed to apply weapon skin after GiveNamedItem.");
					}
				});
			}
			catch (Exception ex)
			{
				Logger.LogWarning(ex, "Failed to process GiveNamedItem skin application.");
			}

			return HookResult.Continue;
		}

		private void OnEntityCreated(CEntityInstance entity)
		{
			var designerName = entity.DesignerName;

			if (designerName.Contains("weapon"))
			{
				var entityHandle = entity.EntityHandle.Raw;
				var generation = _worldGeneration;

				Server.NextWorldUpdate(() =>
				{
					if (_stopping || generation != _worldGeneration)
						return;

					var weaponPointer = EntitySystem.GetEntityByHandle(entityHandle);
					if (weaponPointer is null || weaponPointer == IntPtr.Zero)
						return;

					var weapon = new CBasePlayerWeapon(weaponPointer.Value);
					if (!weapon.IsValid)
						return;

					try
					{
						SteamID? steamid = null;

						if (weapon.OriginalOwnerXuidLow > 0)
							steamid = new SteamID(weapon.OriginalOwnerXuidLow);

						CCSPlayerController? player = null;

						if (steamid != null && steamid.IsValid())
						{
							player = Players.FirstOrDefault(p => p.IsValid && p.SteamID == steamid.SteamId64);

							if (player == null)
								player = Utilities.GetPlayerFromSteamId(weapon.OriginalOwnerXuidLow);
						}
						else
						{
							var owner = weapon.OwnerEntity.Value;
							if (owner != null && owner.IsValid)
								player = Utilities.GetPlayerFromIndex((int)owner.Index);

							if (player == null)
							{
								var gun = weapon.As<CCSWeaponBaseGun>();
								var gunOwner = gun.OwnerEntity.Value;
								if (gunOwner != null && gunOwner.IsValid)
									player = Utilities.GetPlayerFromIndex((int)gunOwner.Index);
							}
						}

						if (string.IsNullOrEmpty(player?.PlayerName)) return;
						if (!Utility.IsPlayerValid(player)) return;

						GivePlayerWeaponSkin(player, weapon);
					}
					catch (Exception)
					{
					}
				});
			}
		}

		private void OnTick()
		{
			if (!Config.Additional.ShowSkinImage) return;

			foreach (var player in Players)
			{
				if (_playerWeaponImage.TryGetValue(player.Slot, out var value) && !string.IsNullOrEmpty(value))
				{
					player.PrintToCenterHtml("<img src='{PATH}'</img>".Replace("{PATH}", value));
				}
			}
		}
		
		[GameEventHandler]
		public HookResult OnItemPickup(EventItemPickup @event, GameEventInfo _)
		{
			// if (!IsWindows) return HookResult.Continue;
			var player = @event.Userid;
			if (player == null || !player.IsValid || player.IsBot) return HookResult.Continue;
			if (!@event.Item.Contains("knife")) return HookResult.Continue;
		
			var weaponDefIndex = (int)@event.Defindex;
				
			if (!HasChangedKnife(player, out var _) || !HasChangedPaint(player, weaponDefIndex, out var _))
				return HookResult.Continue;
			
			if (player is { Connected: PlayerConnectedState.Connected, PawnIsAlive: true, PlayerPawn.IsValid: true })
			{
				GiveOnItemPickup(player);
			}
		
			return HookResult.Continue;
		}

		private HookResult OnPlayerDeath(EventPlayerDeath @event, GameEventInfo info)
		{
			CCSPlayerController? player = @event.Attacker;
			CCSPlayerController? victim = @event.Userid;

			if (player is null || !player.IsValid)
				return HookResult.Continue;
			
			if (victim == null || !victim.IsValid || victim == player)
				return HookResult.Continue;
			
			CBasePlayerWeapon? weapon = player.PlayerPawn.Value?.WeaponServices?.ActiveWeapon.Value;

			if (weapon == null || !weapon.IsValid) return HookResult.Continue;

			var weaponHandle = weapon.EntityHandle.Raw;
			var weaponPointer = EntitySystem.GetEntityByHandle(weaponHandle);
			if (weaponPointer is null || weaponPointer == IntPtr.Zero)
				return HookResult.Continue;

			var currentWeapon = new CBasePlayerWeapon(weaponPointer.Value);
			if (!currentWeapon.IsValid)
				return HookResult.Continue;

			int weaponDefIndex = currentWeapon.AttributeManager.Item.ItemDefinitionIndex;

			if (!HasChangedPaint(player, weaponDefIndex, out var weaponInfo) || weaponInfo == null)
				return HookResult.Continue;
				
			if (!weaponInfo.StatTrak) return HookResult.Continue;
			
			weaponInfo.StatTrakCount += 1;
				
			CAttributeListSetOrAddAttributeValueByName.Invoke(currentWeapon.AttributeManager.Item.NetworkedDynamicAttributes.Handle, "kill eater", ViewAsFloat((uint)weaponInfo.StatTrakCount));
			CAttributeListSetOrAddAttributeValueByName.Invoke(currentWeapon.AttributeManager.Item.NetworkedDynamicAttributes.Handle, "kill eater score type", 0);
				
			CAttributeListSetOrAddAttributeValueByName.Invoke(currentWeapon.AttributeManager.Item.AttributeList.Handle, "kill eater", ViewAsFloat((uint)weaponInfo.StatTrakCount));
			CAttributeListSetOrAddAttributeValueByName.Invoke(currentWeapon.AttributeManager.Item.AttributeList.Handle, "kill eater score type", 0);

			return HookResult.Continue;
		}

		public override void Unload(bool hotReload)
		{
			_stopping = true;
			_worldGeneration++;

			try
			{
				VirtualFunctions.GiveNamedItemFunc.Unhook(OnGiveNamedItemPost, HookMode.Post);
			}
			catch (Exception ex)
			{
				Logger.LogWarning(ex, "Failed to unhook GiveNamedItem during unload.");
			}
		}

		private void RegisterListeners()
		{
			_stopping = false;
			RegisterListener<Listeners.OnMapStart>(OnMapStart);

			RegisterEventHandler<EventPlayerSpawn>(OnPlayerSpawn);
			RegisterEventHandler<EventRoundStart>(OnRoundStart);
			RegisterEventHandler<EventRoundEnd>(OnRoundEnd);
			RegisterEventHandler<EventRoundMvp>(OnRoundMvp);
			RegisterListener<Listeners.OnEntitySpawned>(OnEntityCreated);
			RegisterEventHandler<EventPlayerDeath>(OnPlayerDeath);

			if (Config.Additional.ShowSkinImage)
				RegisterListener<Listeners.OnTick>(OnTick);

			VirtualFunctions.GiveNamedItemFunc.Hook(OnGiveNamedItemPost, HookMode.Post);
		}
	}
}
