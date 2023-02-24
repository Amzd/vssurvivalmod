﻿using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace Vintagestory.GameContent
{
    public class BlockEntityDisplayCase : BlockEntityDisplay
    {
        public override string InventoryClassName => "displaycase";
        protected InventoryGeneric inventory;
        public override InventoryBase Inventory => inventory;


        public BlockEntityDisplayCase()
        {
            inventory = new InventoryDisplayed(this, 4, "displaycase-0", null, null);
        }

        internal bool OnInteract(IPlayer byPlayer, BlockSelection blockSel)
        {
            ItemSlot slot = byPlayer.InventoryManager.ActiveHotbarSlot;

            if (slot.Empty)
            {
                if (TryTake(byPlayer, blockSel))
                {
                    return true;
                }
                return false;
            }
            else
            {
                CollectibleObject colObj = slot.Itemstack.Collectible;
                if (colObj.Attributes != null && colObj.Attributes["displaycaseable"].AsBool(false) == true)
                {
                    AssetLocation sound = slot.Itemstack?.Block?.Sounds?.Place;

                    if (TryPut(slot, blockSel))
                    {
                        Api.World.PlaySoundAt(sound != null ? sound : new AssetLocation("sounds/player/build"), byPlayer.Entity, byPlayer, true, 16);
                        return true;
                    }

                    return false;
                }

                (Api as ICoreClientAPI)?.TriggerIngameError(this, "doesnotfit", Lang.Get("This item does not fit into the display case."));
                return true;
            }
        }



        private bool TryPut(ItemSlot slot, BlockSelection blockSel)
        {
            int index = blockSel.SelectionBoxIndex;

            if (inventory[index].Empty)
            {
                int moved = slot.TryPutInto(Api.World, inventory[index]);

                if (moved > 0)
                {
                    updateMesh(index);
                    
                    MarkDirty(true);
                }
                
                return moved > 0;
            }

            return false;
        }

        private bool TryTake(IPlayer byPlayer, BlockSelection blockSel)
        {
            int index = blockSel.SelectionBoxIndex;

            if (!inventory[index].Empty)
            {
                ItemStack stack = inventory[index].TakeOut(1);
                if (byPlayer.InventoryManager.TryGiveItemstack(stack))
                {
                    AssetLocation sound = stack.Block?.Sounds?.Place;
                    Api.World.PlaySoundAt(sound != null ? sound : new AssetLocation("sounds/player/build"), byPlayer.Entity, byPlayer, true, 16);
                }

                if (stack.StackSize > 0)
                {
                    Api.World.SpawnItemEntity(stack, Pos.ToVec3d().Add(0.5, 0.5, 0.5));
                }

                updateMesh(index);
                MarkDirty(true);
                return true;
            }

            return false;
        }

            

        public override void GetBlockInfo(IPlayer forPlayer, StringBuilder sb)
        {
            base.GetBlockInfo(forPlayer, sb);

            sb.AppendLine();

            if (forPlayer?.CurrentBlockSelection == null) return;

            int index = forPlayer.CurrentBlockSelection.SelectionBoxIndex;

            if (!inventory[index].Empty)
            {
                sb.AppendLine(inventory[index].Itemstack.GetName());
            }
        }

        protected override float[][] genTransformationMatrices()
        {
            float[][] tfMatrices = new float[4][];

            for (int index = 0; index < 4; index++)
            {
                float x = (index % 2 == 0) ? 5 / 16f : 11 / 16f;
                float y = 1.01f / 16f;
                float z = (index > 1) ? 11 / 16f : 5 / 16f;

                int rnd = GameMath.MurmurHash3Mod(Pos.X, Pos.Y + index * 50, Pos.Z, 30) - 15;
                var collObjAttr = inventory[index]?.Itemstack?.Collectible?.Attributes;
                if (collObjAttr != null && collObjAttr["randomizeInDisplayCase"].AsBool(true) == false)
                {
                    rnd = 0;
                }

                float degY = (45 + rnd);

                tfMatrices[index] = 
                    new Matrixf()
                    .Translate(0.5f, 0, 0.5f)
                    .Translate(x - 0.5f, y, z - 0.5f)
                    .RotateYDeg(degY)
                    .Scale(0.75f, 0.75f, 0.75f)
                    .Translate(-0.5f, 0, -0.5f)
                    .Values
                ;
            }

            return tfMatrices;
        }


        
    }

}
