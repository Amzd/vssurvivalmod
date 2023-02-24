﻿using Vintagestory.API.Common;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace Vintagestory.ServerMods
{

    public class DebugSystem : ModSystem
    {
        ICoreAPI api;


        public override bool ShouldLoad(EnumAppSide forSide)
        {
            return true;
        }

        public override void StartServerSide(ICoreServerAPI api)
        {
            this.api = api;
            
            //api.RegisterCommand("anvildebug", "Anvil debug info", "", onAnvilDebug, "worldedit");
        }

        private void onAnvilDebug(IServerPlayer player, int groupId, CmdArgs args)
        {
            if (player.CurrentBlockSelection?.Position != null)
            {
                BlockEntityAnvil bea = api.World.BlockAccessor.GetBlockEntity(player.CurrentBlockSelection.Position) as BlockEntityAnvil;

                if (bea == null)
                {
                    player.SendMessage(groupId, "Not looking at an anvil", EnumChatType.CommandError);
                    return;
                }

                player.SendMessage(groupId, bea.PrintDebugText(), EnumChatType.CommandSuccess);
            }
            
        }
    }
}
