﻿#region Copyright & License Information
/*
 * Copyright 2007-2011 The OpenRA Developers (see AUTHORS)
 * This file is part of OpenRA, which is free software. It is made
 * available to you under the terms of the GNU General Public License
 * as published by the Free Software Foundation. For more information,
 * see COPYING.
 */
#endregion

using System.Collections.Generic;
using System.Linq;
using OpenRA.Mods.RA.Orders;
using OpenRA.Traits;

namespace OpenRA.Mods.RA
{
	class PrimaryBuildingInfo : TraitInfo<PrimaryBuilding> { }

	class PrimaryBuilding : IIssueOrder, IResolveOrder, ITags
	{
		bool isPrimary = false;
		public bool IsPrimary { get { return isPrimary; } }

		public IEnumerable<TagType> GetTags()
		{
			yield return (isPrimary) ? TagType.Primary : TagType.None;
		}

		public IEnumerable<IOrderTargeter> Orders
		{
			get { yield return new DeployOrderTargeter( "PrimaryProducer", 1 ); }
		}

		public Order IssueOrder( Actor self, IOrderTargeter order, Target target, bool queued )
		{
			if( order.OrderID == "PrimaryProducer" )
				return new Order( order.OrderID, self, false );

			return null;
		}

		public void ResolveOrder(Actor self, Order order)
		{
			if (order.OrderString == "PrimaryProducer")
				SetPrimaryProducer(self, !isPrimary);
		}

		public void SetPrimaryProducer(Actor self, bool state)
		{
			if (state == false)
			{
				isPrimary = false;
				return;
			}

			// THIS IS SHIT
			// Cancel existing primaries
			foreach (var p in self.Info.Traits.Get<ProductionInfo>().Produces)
				foreach (var b in self.World
					.ActorsWithTrait<PrimaryBuilding>()
					.Where(a => a.Actor.Owner == self.Owner)
					.Where(x => x.Trait.IsPrimary
						&& (x.Actor.Info.Traits.Get<ProductionInfo>().Produces.Contains(p))))
					b.Trait.SetPrimaryProducer(b.Actor, false);

			isPrimary = true;

			var eva = self.World.WorldActor.Info.Traits.Get<EvaAlertsInfo>();
			Sound.PlayToPlayer(self.Owner, eva.PrimaryBuildingSelected);
		}
	}

	static class PrimaryExts
	{
		public static bool IsPrimaryBuilding(this Actor a)
		{
			var pb = a.TraitOrDefault<PrimaryBuilding>();
			return pb != null && pb.IsPrimary;
		}
	}
}
