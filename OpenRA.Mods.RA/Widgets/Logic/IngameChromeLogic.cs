#region Copyright & License Information
/*
 * Copyright 2007-2011 The OpenRA Developers (see AUTHORS)
 * This file is part of OpenRA, which is free software. It is made 
 * available to you under the terms of the GNU General Public License
 * as published by the Free Software Foundation. For more information,
 * see COPYING.
 */
#endregion

using OpenRA.Traits;
using OpenRA.Widgets;
using System.Drawing;

namespace OpenRA.Mods.RA.Widgets.Logic
{
	public class IngameChromeLogic
	{
		Widget gameRoot;
		
		[ObjectCreator.UseCtor]
		public IngameChromeLogic( [ObjectCreator.Param] World world )
		{
			Game.AddChatLine += AddChatLine;
			Game.BeforeGameStart += UnregisterEvents;
			
			var r = Widget.RootWidget;
			gameRoot = r.GetWidget("INGAME_ROOT");
			var optionsBG = gameRoot.GetWidget("INGAME_OPTIONS_BG");
			
			r.GetWidget("INGAME_OPTIONS_BUTTON").OnMouseUp = mi => {
				optionsBG.Visible = !optionsBG.Visible;
				return true;
			};
			
			optionsBG.GetWidget("DISCONNECT").OnMouseUp = mi => {
				optionsBG.Visible = false;
				Game.Disconnect();
				Game.LoadShellMap();
				Widget.CloseWindow();
				Widget.OpenWindow("MAINMENU_BG");
				return true;
			};
			
			optionsBG.GetWidget("SETTINGS").OnMouseUp = mi => {
				Widget.OpenWindow("SETTINGS_MENU");
				return true;
			};

			optionsBG.GetWidget("MUSIC").OnMouseUp = mi => {
				Widget.OpenWindow("MUSIC_MENU");
				return true;
			};
			
			optionsBG.GetWidget("RESUME").OnMouseUp = mi =>
			{
				optionsBG.Visible = false;
				return true;
			};

			optionsBG.GetWidget("SURRENDER").OnMouseUp = mi =>
			{
				world.IssueOrder(new Order("Surrender", world.LocalPlayer.PlayerActor, false));
				return true;
			};

			optionsBG.GetWidget("SURRENDER").IsVisible = () => (world.LocalPlayer != null && world.LocalPlayer.WinState == WinState.Undefined);
			
			optionsBG.GetWidget("QUIT").OnMouseUp = mi => {
				Game.Exit();
				return true;
			};

			var postgameBG = gameRoot.GetWidget("POSTGAME_BG");
			var postgameText = postgameBG.GetWidget<LabelWidget>("TEXT");
			postgameBG.IsVisible = () =>
			{
				return world.LocalPlayer != null && world.LocalPlayer.WinState != WinState.Undefined;
			};
			
			postgameText.GetText = () =>
			{
				var state = world.LocalPlayer.WinState;
				return (state == WinState.Undefined)? "" :
								((state == WinState.Lost)? "YOU ARE DEFEATED" : "YOU ARE VICTORIOUS");
			};
		}
		
		public void UnregisterEvents()
		{
			Game.AddChatLine -= AddChatLine;
			Game.BeforeGameStart -= UnregisterEvents;
		}
		
		void AddChatLine(Color c, string from, string text)
		{
			gameRoot.GetWidget<ChatDisplayWidget>("CHAT_DISPLAY").AddLine(c, from, text);
		}
	}
}