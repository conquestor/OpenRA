#region Copyright & License Information
/*
 * Copyright 2007-2011 The OpenRA Developers (see AUTHORS)
 * This file is part of OpenRA, which is free software. It is made
 * available to you under the terms of the GNU General Public License
 * as published by the Free Software Foundation. For more information,
 * see COPYING.
 */
#endregion

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using OpenRA.Network;
using OpenRA.Traits;
using OpenRA.Widgets;

namespace OpenRA.Mods.RA.Widgets.Logic
{
	public class LobbyLogic
	{
		Widget EditablePlayerTemplate, NonEditablePlayerTemplate, EmptySlotTemplate,
			   EditableSpectatorTemplate, NonEditableSpectatorTemplate, NewSpectatorTemplate;
		ScrollPanelWidget chatPanel;
		Widget chatTemplate;

		ScrollPanelWidget Players;
		Dictionary<string, string> CountryNames;
		string MapUid;
		Map Map;

		ColorPickerPaletteModifier PlayerPalettePreview;

		readonly Action OnGameStart;
		readonly Action onExit;
		readonly OrderManager orderManager;

		// Listen for connection failures
		void ConnectionStateChanged(OrderManager om)
		{
			if (om.Connection.ConnectionState == ConnectionState.NotConnected)
			{
				// Show connection failed dialog
				CloseWindow();

				Action onConnect = () =>
				{
					Game.OpenWindow("SERVER_LOBBY", new WidgetArgs()
					{
						{ "onExit", onExit },
						{ "onStart", OnGameStart },
						{ "addBots", false }
					});
				};

				Action onRetry = () =>
				{
					CloseWindow();
					ConnectionLogic.Connect(om.Host, om.Port, onConnect, onExit);
				};

				Ui.OpenWindow("CONNECTIONFAILED_PANEL", new WidgetArgs()
				{
					{ "onAbort", onExit },
					{ "onRetry", onRetry },
					{ "host", om.Host },
					{ "port", om.Port }
				});
			}
		}

		void CloseWindow()
		{
			Game.LobbyInfoChanged -= UpdateCurrentMap;
			Game.LobbyInfoChanged -= UpdatePlayerList;
			Game.BeforeGameStart -= OnGameStart;
			Game.AddChatLine -= AddChatLine;
			Game.ConnectionStateChanged -= ConnectionStateChanged;

			Ui.CloseWindow();
		}

		[ObjectCreator.UseCtor]
		internal LobbyLogic(Widget widget, World world, OrderManager orderManager,
			Action onExit, Action onStart, bool addBots)
		{
			var lobby = widget;
			this.orderManager = orderManager;
			this.OnGameStart = () => { CloseWindow(); onStart(); };
			this.onExit = onExit;

			Game.LobbyInfoChanged += UpdateCurrentMap;
			Game.LobbyInfoChanged += UpdatePlayerList;
			Game.BeforeGameStart += OnGameStart;
			Game.AddChatLine += AddChatLine;
			Game.ConnectionStateChanged += ConnectionStateChanged;

			UpdateCurrentMap();
			PlayerPalettePreview = world.WorldActor.Trait<ColorPickerPaletteModifier>();
			PlayerPalettePreview.Ramp = Game.Settings.Player.ColorRamp;
			Players = lobby.GetWidget<ScrollPanelWidget>("PLAYERS");
			EditablePlayerTemplate = Players.GetWidget("TEMPLATE_EDITABLE_PLAYER");
			NonEditablePlayerTemplate = Players.GetWidget("TEMPLATE_NONEDITABLE_PLAYER");
			EmptySlotTemplate = Players.GetWidget("TEMPLATE_EMPTY");
			EditableSpectatorTemplate = Players.GetWidget("TEMPLATE_EDITABLE_SPECTATOR");
			NonEditableSpectatorTemplate = Players.GetWidget("TEMPLATE_NONEDITABLE_SPECTATOR");
			NewSpectatorTemplate = Players.GetWidget("TEMPLATE_NEW_SPECTATOR");

			var mapPreview = lobby.GetWidget<MapPreviewWidget>("MAP_PREVIEW");
			mapPreview.IsVisible = () => Map != null;
			mapPreview.Map = () => Map;
			mapPreview.OnMouseDown = mi => LobbyUtils.SelectSpawnPoint( orderManager, mapPreview, Map, mi );
			mapPreview.SpawnColors = () => LobbyUtils.GetSpawnColors( orderManager, Map );

			var mapTitle = lobby.GetWidget<LabelWidget>("MAP_TITLE");
			if (mapTitle != null)
			{
				mapTitle.IsVisible = () => Map != null;
				mapTitle.GetText = () => Map.Title;
			}

			CountryNames = Rules.Info["world"].Traits.WithInterface<CountryInfo>()
				.Where(c => c.Selectable)
				.ToDictionary(a => a.Race, a => a.Name);
			CountryNames.Add("random", "Any");

			var mapButton = lobby.GetWidget<ButtonWidget>("CHANGEMAP_BUTTON");
			mapButton.OnClick = () =>
			{
				var onSelect = new Action<Map>(m =>
				{
					orderManager.IssueOrder(Order.Command("map " + m.Uid));
					Game.Settings.Server.Map = m.Uid;
					Game.Settings.Save();
				});

				Ui.OpenWindow("MAPCHOOSER_PANEL", new WidgetArgs()
				{
					{ "initialMap", Map.Uid },
					{ "onExit", () => {} },
					{ "onSelect", onSelect }
				});
			};
			mapButton.IsVisible = () => mapButton.Visible && Game.IsHost;

			var disconnectButton = lobby.GetWidget<ButtonWidget>("DISCONNECT_BUTTON");
			disconnectButton.OnClick = () => { CloseWindow(); onExit(); };

			var gameStarting = false;

			var allowCheats = lobby.GetWidget<CheckboxWidget>("ALLOWCHEATS_CHECKBOX");
			allowCheats.IsChecked = () => orderManager.LobbyInfo.GlobalSettings.AllowCheats;
			allowCheats.IsDisabled = () => !Game.IsHost || gameStarting || orderManager.LocalClient == null
				|| orderManager.LocalClient.IsReady;
			allowCheats.OnClick = () =>	orderManager.IssueOrder(Order.Command(
						"allowcheats {0}".F(!orderManager.LobbyInfo.GlobalSettings.AllowCheats)));

			var startGameButton = lobby.GetWidget<ButtonWidget>("START_GAME_BUTTON");
			startGameButton.IsVisible = () => Game.IsHost;
			startGameButton.IsDisabled = () => gameStarting;
			startGameButton.OnClick = () =>
			{
				gameStarting = true;
				orderManager.IssueOrder(Order.Command("startgame"));
			};

			bool teamChat = false;
			var chatLabel = lobby.GetWidget<LabelWidget>("LABEL_CHATTYPE");
			var chatTextField = lobby.GetWidget<TextFieldWidget>("CHAT_TEXTFIELD");

			chatTextField.OnEnterKey = () =>
			{
				if (chatTextField.Text.Length == 0)
					return true;

				orderManager.IssueOrder(Order.Chat(teamChat, chatTextField.Text));
				chatTextField.Text = "";
				return true;
			};

			chatTextField.OnTabKey = () =>
			{
				teamChat ^= true;
				chatLabel.Text = (teamChat) ? "Team:" : "Chat:";
				return true;
			};

			chatPanel = lobby.GetWidget<ScrollPanelWidget>("CHAT_DISPLAY");
			chatTemplate = chatPanel.GetWidget("CHAT_TEMPLATE");
			chatPanel.RemoveChildren();

			var musicButton = lobby.GetWidget<ButtonWidget>("MUSIC_BUTTON");
			if (musicButton != null)
				musicButton.OnClick = () => Ui.OpenWindow("MUSIC_PANEL", new WidgetArgs
					{ { "onExit", () => {} } });

			// Add a bot on the first lobbyinfo update
			if (addBots)
				Game.LobbyInfoChanged += WidgetUtils.Once(() =>
				{
					var slot = orderManager.LobbyInfo.FirstEmptySlot();
					var bot = Rules.Info["player"].Traits.WithInterface<IBotInfo>().Select(t => t.Name).FirstOrDefault();
					if (slot != null && bot != null)
						orderManager.IssueOrder(Order.Command("slot_bot {0} {1}".F(slot, bot)));
				});
		}

		void AddChatLine(Color c, string from, string text)
		{
			var template = chatTemplate.Clone();
			var nameLabel = template.GetWidget<LabelWidget>("NAME");
			var timeLabel = template.GetWidget<LabelWidget>("TIME");
			var textLabel = template.GetWidget<LabelWidget>("TEXT");

			var name = from + ":";
			var font = Game.Renderer.Fonts[nameLabel.Font];
			var nameSize = font.Measure(from);

			var time = DateTime.Now;
			timeLabel.GetText = () => "{0:D2}:{1:D2}".F(time.Hour, time.Minute);

			nameLabel.GetColor = () => c;
			nameLabel.GetText = () => name;
			nameLabel.Bounds.Width = nameSize.X;
			textLabel.Bounds.X += nameSize.X;
			textLabel.Bounds.Width -= nameSize.X;

			// Hack around our hacky wordwrap behavior: need to resize the widget to fit the text
			text = WidgetUtils.WrapText(text, textLabel.Bounds.Width, font);
			textLabel.GetText = () => text;
			var dh = font.Measure(text).Y - textLabel.Bounds.Height;
			if (dh > 0)
			{
				textLabel.Bounds.Height += dh;
				template.Bounds.Height += dh;
			}

			chatPanel.AddChild(template);
			chatPanel.ScrollToBottom();
			Sound.Play("scold1.aud");
		}

		void UpdateCurrentMap()
		{
			if (MapUid == orderManager.LobbyInfo.GlobalSettings.Map) return;
			MapUid = orderManager.LobbyInfo.GlobalSettings.Map;
			Map = new Map(Game.modData.AvailableMaps[MapUid].Path);

			var title = Ui.Root.GetWidget<LabelWidget>("TITLE");
			title.Text = orderManager.LobbyInfo.GlobalSettings.ServerName;
		}

		void UpdatePlayerList()
		{
			// This causes problems for people who are in the process of editing their names (the widgets vanish from beneath them)
			// Todo: handle this nicer
			Players.RemoveChildren();

			foreach (var kv in orderManager.LobbyInfo.Slots)
			{
				var key = kv.Key;
				var slot = kv.Value;
				var client = orderManager.LobbyInfo.ClientInSlot(key);
				Widget template;

				// Empty slot
				if (client == null)
				{
					template = EmptySlotTemplate.Clone();
					Func<string> getText = () => slot.Closed ? "Closed" : "Open";
					var ready = orderManager.LocalClient.IsReady;

					if (Game.IsHost)
					{
						var name = template.GetWidget<DropDownButtonWidget>("NAME_HOST");
						name.IsVisible = () => true;
						name.IsDisabled = () => ready;
						name.GetText = getText;
						name.OnMouseDown = _ => LobbyUtils.ShowSlotDropDown(name, slot, client, orderManager);
					}
					else
					{
						var name = template.GetWidget<LabelWidget>("NAME");
						name.IsVisible = () => true;
						name.GetText = getText;
					}

					var join = template.GetWidget<ButtonWidget>("JOIN");
					join.IsVisible = () => !slot.Closed;
					join.IsDisabled = () => ready;
					join.OnClick = () => orderManager.IssueOrder(Order.Command("slot " + key));
				}
				// Editable player in slot
				else if ((client.Index == orderManager.LocalClient.Index) ||
						 (client.Bot != null && Game.IsHost))
				{
					template = EditablePlayerTemplate.Clone();
					var botReady = client.Bot != null && Game.IsHost && orderManager.LocalClient.IsReady;
					var ready = botReady || client.IsReady;

					if (client.Bot != null)
					{
						var name = template.GetWidget<DropDownButtonWidget>("BOT_DROPDOWN");
						name.IsVisible = () => true;
						name.IsDisabled = () => ready;
						name.GetText = () => client.Name;
						name.OnMouseDown = _ => LobbyUtils.ShowSlotDropDown(name, slot, client, orderManager);
					}
					else
					{
						var name = template.GetWidget<TextFieldWidget>("NAME");
						name.IsVisible = () => true;
						name.IsDisabled = () => ready;
						LobbyUtils.SetupNameWidget(orderManager, client, name);
					}

					var color = template.GetWidget<DropDownButtonWidget>("COLOR");
					color.IsDisabled = () => slot.LockColor || ready;
					color.OnMouseDown = _ => LobbyUtils.ShowColorDropDown(color, client, orderManager, PlayerPalettePreview);

					var colorBlock = color.GetWidget<ColorBlockWidget>("COLORBLOCK");
					colorBlock.GetColor = () => client.ColorRamp.GetColor(0);

					var faction = template.GetWidget<DropDownButtonWidget>("FACTION");
					faction.IsDisabled = () => slot.LockRace || ready;
					faction.OnMouseDown = _ => LobbyUtils.ShowRaceDropDown(faction, client, orderManager, CountryNames);

					var factionname = faction.GetWidget<LabelWidget>("FACTIONNAME");
					factionname.GetText = () => CountryNames[client.Country];
					var factionflag = faction.GetWidget<ImageWidget>("FACTIONFLAG");
					factionflag.GetImageName = () => client.Country;
					factionflag.GetImageCollection = () => "flags";

					var team = template.GetWidget<DropDownButtonWidget>("TEAM");
					team.IsDisabled = () => slot.LockTeam || ready;
					team.OnMouseDown = _ => LobbyUtils.ShowTeamDropDown(team, client, orderManager, Map);
					team.GetText = () => (client.Team == 0) ? "-" : client.Team.ToString();

					if (client.Bot == null)
					{
						// local player
						var status = template.GetWidget<CheckboxWidget>("STATUS_CHECKBOX");
						status.IsChecked = () => ready;
						status.IsVisible = () => true;
						status.OnClick += CycleReady;
					}
					else // Bot
						template.GetWidget<ImageWidget>("STATUS_IMAGE").IsVisible = () => true;
				}
				else
				{	// Non-editable player in slot
					template = NonEditablePlayerTemplate.Clone();
					template.GetWidget<LabelWidget>("NAME").GetText = () => client.Name;
					var color = template.GetWidget<ColorBlockWidget>("COLOR");
					color.GetColor = () => client.ColorRamp.GetColor(0);

					var faction = template.GetWidget<LabelWidget>("FACTION");
					var factionname = faction.GetWidget<LabelWidget>("FACTIONNAME");
					factionname.GetText = () => CountryNames[client.Country];
					var factionflag = faction.GetWidget<ImageWidget>("FACTIONFLAG");
					factionflag.GetImageName = () => client.Country;
					factionflag.GetImageCollection = () => "flags";

					var team = template.GetWidget<LabelWidget>("TEAM");
					team.GetText = () => (client.Team == 0) ? "-" : client.Team.ToString();

					template.GetWidget<ImageWidget>("STATUS_IMAGE").IsVisible = () =>
						client.Bot != null || client.IsReady;

					var kickButton = template.GetWidget<ButtonWidget>("KICK");
					kickButton.IsVisible = () => Game.IsHost && client.Index != orderManager.LocalClient.Index;
					kickButton.IsDisabled = () => orderManager.LocalClient.IsReady;
					kickButton.OnClick = () => orderManager.IssueOrder(Order.Command("kick " + client.Index));
				}

				template.IsVisible = () => true;
				Players.AddChild(template);
			}

			// Add spectators
			foreach (var client in orderManager.LobbyInfo.Clients.Where(client => client.Slot == null))
			{
				Widget template;
				var c = client;
				var ready = c.IsReady;

				// Editable spectator
				if (c.Index == orderManager.LocalClient.Index)
				{
					template = EditableSpectatorTemplate.Clone();
					var name = template.GetWidget<TextFieldWidget>("NAME");
					name.IsDisabled = () => ready;
					LobbyUtils.SetupNameWidget(orderManager, c, name);

					var color = template.GetWidget<DropDownButtonWidget>("COLOR");
					color.IsDisabled = () => ready;
					color.OnMouseDown = _ => LobbyUtils.ShowColorDropDown(color, c, orderManager, PlayerPalettePreview);

					var colorBlock = color.GetWidget<ColorBlockWidget>("COLORBLOCK");
					colorBlock.GetColor = () => c.ColorRamp.GetColor(0);

					var status = template.GetWidget<CheckboxWidget>("STATUS_CHECKBOX");
					status.IsChecked = () => ready;
					status.OnClick += CycleReady;
				}
				// Non-editable spectator
				else
				{
					template = NonEditableSpectatorTemplate.Clone();
					template.GetWidget<LabelWidget>("NAME").GetText = () => c.Name;
					var color = template.GetWidget<ColorBlockWidget>("COLOR");
					color.GetColor = () => c.ColorRamp.GetColor(0);

					template.GetWidget<ImageWidget>("STATUS_IMAGE").IsVisible = () => c.Bot != null || c.IsReady;

					var kickButton = template.GetWidget<ButtonWidget>("KICK");
					kickButton.IsVisible = () => Game.IsHost && c.Index != orderManager.LocalClient.Index;
					kickButton.IsDisabled = () => orderManager.LocalClient.IsReady;
					kickButton.OnClick = () => orderManager.IssueOrder(Order.Command("kick " + c.Index));
				}

				template.IsVisible = () => true;
				Players.AddChild(template);
			}

			// Spectate button
			if (orderManager.LocalClient.Slot != null)
			{
				var spec = NewSpectatorTemplate.Clone();
				var btn = spec.GetWidget<ButtonWidget>("SPECTATE");
				btn.OnClick = () => orderManager.IssueOrder(Order.Command("spectate"));
				btn.IsDisabled = () => orderManager.LocalClient.IsReady;
				spec.IsVisible = () => true;
				Players.AddChild(spec);
			}
		}

		bool SpawnPointAvailable(int index) { return (index == 0) || orderManager.LobbyInfo.Clients.All(c => c.SpawnPoint != index); }

		void CycleReady()
		{
			orderManager.IssueOrder(Order.Command("ready"));
		}
	}
}
