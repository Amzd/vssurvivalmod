﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;

namespace Vintagestory.GameContent
{
    public class BlockDisplayCase : Block
    {
        WorldInteraction[] interactions;

        public override void OnLoaded(ICoreAPI api)
        {
            base.OnLoaded(api);

            if (api.Side != EnumAppSide.Client) return;
            ICoreClientAPI capi = api as ICoreClientAPI;

            // NOTE: this currently has no effect, because a BlockEntityDisplayCase is not a BlockEntityBomb
            interactions = ObjectCacheUtil.GetOrCreate(api, "displayCaseInteractions", () =>
            {
                List<ItemStack> canIgniteStacks = BlockBehaviorCanIgnite.CanIgniteStacks(api, false);

                return new WorldInteraction[] {
                    new WorldInteraction()
                    {
                        MouseButton = EnumMouseButton.Right,
                        ActionLangCode = "blockhelp-bomb-ignite",
                        Itemstacks = canIgniteStacks.ToArray(),
                        GetMatchingStacks = (wi, bs, es) => {
                            BlockEntityBomb bebomb = api.World.BlockAccessor.GetBlockEntity(bs.Position) as BlockEntityBomb;
                            return bebomb == null || bebomb.IsLit ? null : wi.Itemstacks;
                        }
                    }
                };
            });
        }

        public override bool DoParticalSelection(IWorldAccessor world, BlockPos pos)
        {
            return true;
        }

        public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
        {
            BlockEntityDisplayCase bedc = world.BlockAccessor.GetBlockEntity(blockSel.Position) as BlockEntityDisplayCase;
            if (bedc != null) return bedc.OnInteract(byPlayer, blockSel);


            return base.OnBlockInteractStart(world, byPlayer, blockSel);
        }


        public override WorldInteraction[] GetPlacedBlockInteractionHelp(IWorldAccessor world, BlockSelection selection, IPlayer forPlayer)
        {
            return interactions.Append(base.GetPlacedBlockInteractionHelp(world, selection, forPlayer));
        }
    }
}
