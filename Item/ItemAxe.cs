﻿using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;


namespace Vintagestory.GameContent
{
    public class ItemAxe : Item
    {
        static SimpleParticleProperties dustParticles = new SimpleParticleProperties()
        {
            MinPos = new Vec3d(),
            AddPos = new Vec3d(),
            MinQuantity = 0,
            AddQuantity = 3,
            Color = ColorUtil.ToRgba(100, 200, 200, 200),
            GravityEffect = 1f,
            WithTerrainCollision = true,
            ParticleModel = EnumParticleModel.Quad,
            LifeLength = 0.5f,
            MinVelocity = new Vec3f(-1, 2, -1),
            AddVelocity = new Vec3f(2, 0, 2),
            MinSize = 0.07f,
            MaxSize = 0.1f,
            WindAffected = true
        };

        static ItemAxe()
        {
            dustParticles.ParticleModel = EnumParticleModel.Quad;
            dustParticles.AddPos.Set(1, 1, 1);
            dustParticles.MinQuantity = 2;
            dustParticles.AddQuantity = 12;
            dustParticles.LifeLength = 4f;
            dustParticles.MinSize = 0.2f;
            dustParticles.MaxSize = 0.5f;
            dustParticles.MinVelocity.Set(-0.4f, -0.4f, -0.4f);
            dustParticles.AddVelocity.Set(0.8f, 1.2f, 0.8f);
            dustParticles.DieOnRainHeightmap = false;
            dustParticles.WindAffectednes = 0.5f;
        }


        public override void OnHeldAttackStart(ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, ref EnumHandHandling handling)
        {
            base.OnHeldAttackStart(slot, byEntity, blockSel, entitySel, ref handling);
        }

        public override bool OnHeldAttackStep(float secondsPassed, ItemSlot slot, EntityAgent byEntity, BlockSelection blockSelection, EntitySelection entitySel)
        {
            return base.OnHeldAttackStep(secondsPassed, slot, byEntity, blockSelection, entitySel);
        }

        public override float OnBlockBreaking(IPlayer player, BlockSelection blockSel, ItemSlot itemslot, float remainingResistance, float dt, int counter)
        {
            ITreeAttribute tempAttr = itemslot.Itemstack.TempAttributes;
            int posx = tempAttr.GetInt("lastposX", -1);
            int posy = tempAttr.GetInt("lastposY", -1);
            int posz = tempAttr.GetInt("lastposZ", -1);
            float treeResistance;

            BlockPos pos = blockSel.Position;

            if (pos.X != posx || pos.Y != posy || pos.Z != posz || counter % 30 == 0)
            {
                FindTree(player.Entity.World, pos, out int baseResistance, out int woodTier);
                if (ToolTier < woodTier - 3) return remainingResistance;   // stone axe cannot cut tropical woods except Kapok (which is soft); copper/scrap axe cannot cut ebony
                treeResistance = (float)Math.Max(1, Math.Sqrt(baseResistance / 1.45));

                tempAttr.SetFloat("treeResistance", treeResistance);
            }
            else treeResistance = tempAttr.GetFloat("treeResistance", 1);

            tempAttr.SetInt("lastposX", pos.X);
            tempAttr.SetInt("lastposY", pos.Y);
            tempAttr.SetInt("lastposZ", pos.Z);


            return base.OnBlockBreaking(player, blockSel, itemslot, remainingResistance, dt / treeResistance, counter);
        }

        public override bool OnBlockBrokenWith(IWorldAccessor world, Entity byEntity, ItemSlot itemslot, BlockSelection blockSel, float dropQuantityMultiplier = 1)
        {
            IPlayer byPlayer = null;
            if (byEntity is EntityPlayer) byPlayer = byEntity.World.PlayerByUid(((EntityPlayer)byEntity).PlayerUID);

            double windspeed = api.ModLoader.GetModSystem<WeatherSystemBase>()?.WeatherDataSlowAccess.GetWindSpeed(byEntity.SidedPos.XYZ) ?? 0;            

            Stack<BlockPos> foundPositions = FindTree(world, blockSel.Position, out int _, out int woodTier);
            
            if (foundPositions.Count == 0)
            {
                return base.OnBlockBrokenWith(world, byEntity, itemslot, blockSel, dropQuantityMultiplier);
            }

            bool damageable = DamagedBy != null && DamagedBy.Contains(EnumItemDamageSource.BlockBreaking);

            float leavesMul = 1;
            float leavesBranchyMul = 0.8f;
            int blocksbroken = 0;

            bool axeHasDurability = true;
            while (foundPositions.Count > 0) {
                BlockPos pos = foundPositions.Pop();

                Block block = world.BlockAccessor.GetBlock(pos);

                bool isLog = block.BlockMaterial == EnumBlockMaterial.Wood;
                if (isLog && !axeHasDurability) continue;

                blocksbroken++;
                bool isBranchy = block.Code.Path.Contains("branchy");
                bool isLeaves = block.BlockMaterial == EnumBlockMaterial.Leaves;

                world.BlockAccessor.BreakBlock(pos, byPlayer, isLeaves ? leavesMul : (isBranchy ? leavesBranchyMul : 1));

                if (world.Side == EnumAppSide.Client)
                {
                    dustParticles.Color = block.GetRandomColor(world.Api as ICoreClientAPI, pos, BlockFacing.UP);
                    dustParticles.Color |= 255 << 24;
                    dustParticles.MinPos.Set(pos.X, pos.Y, pos.Z);

                    if (block.BlockMaterial == EnumBlockMaterial.Leaves)
                    {
                        dustParticles.GravityEffect = (float)world.Rand.NextDouble() * 0.1f + 0.01f;
                        dustParticles.ParticleModel = EnumParticleModel.Quad;
                        dustParticles.MinVelocity.Set(-0.4f + 4 * (float)windspeed, -0.4f, -0.4f);
                        dustParticles.AddVelocity.Set(0.8f + 4 * (float)windspeed, 1.2f, 0.8f);

                    } else
                    {
                        dustParticles.GravityEffect = 0.8f;
                        dustParticles.ParticleModel = EnumParticleModel.Cube;
                        dustParticles.MinVelocity.Set(-0.4f + (float)windspeed, -0.4f, -0.4f);
                        dustParticles.AddVelocity.Set(0.8f + (float)windspeed, 1.2f, 0.8f);
                    }
                    

                    world.SpawnParticles(dustParticles);
                }


                if (damageable && isLog)
                {
                    DamageItem(world, byEntity, itemslot);
                    if (itemslot.Itemstack == null) axeHasDurability = false;
                }

                if (isLeaves && leavesMul > 0.03f) leavesMul *= 0.85f;
                if (isBranchy && leavesBranchyMul > 0.015f) leavesBranchyMul *= 0.7f;
            }

            if (blocksbroken > 35 && axeHasDurability)
            {
                Vec3d pos = blockSel.Position.ToVec3d().Add(0.5, 0.5, 0.5);
                api.World.PlaySoundAt(new AssetLocation("sounds/effect/treefell"), pos.X, pos.Y, pos.Z, byPlayer, false, 32, GameMath.Clamp(blocksbroken / 100f, 0.25f, 1));
            }
            
            return true;
        }


        /// <summary>
        /// Resistance is based on 1 for leaves, 2 for branchy leaves, and 4-8 for logs depending on woodTier.
        /// WoodTier is 3 for softwoods (Janka hardness up to about 1000), 4 for temperate hardwoods (Janka hardness 1000-2000), 5 for tropical hardwoods (Janka hardness 2000-3000), and 6 for ebony (Janka hardness over 3000)
        /// </summary>
        /// <param name="world"></param>
        /// <param name="startPos"></param>
        /// <param name="resistance"></param>
        /// <param name="woodTier"></param>
        /// <returns></returns>
        public Stack<BlockPos> FindTree(IWorldAccessor world, BlockPos startPos, out int resistance, out int woodTier)
        {
            Queue<Vec4i> queue = new Queue<Vec4i>();
            HashSet<BlockPos> checkedPositions = new HashSet<BlockPos>();
            Stack<BlockPos> foundPositions = new Stack<BlockPos>();
            resistance = 0;
            woodTier = 0;
            
            Block block = world.BlockAccessor.GetBlock(startPos);
            if (block.Code == null) return foundPositions;

            string treeFellingGroupCode = block.Attributes?["treeFellingGroupCode"].AsString();
            int spreadIndex = block.Attributes?["treeFellingGroupSpreadIndex"].AsInt(0) ?? 0;
            if (block.Attributes?["treeFellingCanChop"].AsBool(true) == false) return foundPositions;

            EnumTreeFellingBehavior bh = EnumTreeFellingBehavior.Chop;

            if (block is ICustomTreeFellingBehavior ctfbh)
            {
                bh = ctfbh.GetTreeFellingBehavior(startPos, null, spreadIndex);
                if (bh == EnumTreeFellingBehavior.NoChop)
                {
                    resistance = foundPositions.Count;
                    return foundPositions;
                }
            }


            // Must start with a log
            if (spreadIndex < 2) return foundPositions;
            if (treeFellingGroupCode == null) return foundPositions;

            string treeFellingGroupLeafCode = null;
            queue.Enqueue(new Vec4i(startPos.X, startPos.Y, startPos.Z, spreadIndex));
            foundPositions.Push(startPos);
            checkedPositions.Add(startPos);

            while (queue.Count > 0)
            {
                if (foundPositions.Count > 2500)
                {
                    break;
                }

                Vec4i pos = queue.Dequeue();

                block = world.BlockAccessor.GetBlock(pos.X, pos.Y, pos.Z);

                if (block is ICustomTreeFellingBehavior ctfbhh)
                {
                    bh = ctfbhh.GetTreeFellingBehavior(startPos, null, spreadIndex);
                }
                if (bh == EnumTreeFellingBehavior.NoChop) continue;


                for (int i = 0; i < Vec3i.DirectAndIndirectNeighbours.Length; i++)
                {
                    Vec3i facing = Vec3i.DirectAndIndirectNeighbours[i];
                    BlockPos neibPos = new BlockPos(pos.X + facing.X , pos.Y + facing.Y, pos.Z + facing.Z);
                    
                    float hordist = GameMath.Sqrt(neibPos.HorDistanceSqTo(startPos.X, startPos.Z));
                    float vertdist = (neibPos.Y - startPos.Y);

                    // "only breaks blocks inside an upside down square base pyramid"
                    float f = bh == EnumTreeFellingBehavior.ChopSpreadVertical ? 0.5f : 2;
                    if (hordist - 1 >= f * vertdist) continue;
                    if (checkedPositions.Contains(neibPos)) continue;

                    block = world.BlockAccessor.GetBlock(neibPos);
                    if (block.Code == null || block.Id == 0) continue;

                    string ngcode = block.Attributes?["treeFellingGroupCode"].AsString();

                    // Only break the same type tree blocks
                    if (ngcode != treeFellingGroupCode)
                    {
                        if (ngcode == null) continue;
                        // Leaves now can carry treeSubType value of 1-7 therefore do a separate check for the leaves
                        if (ngcode != treeFellingGroupLeafCode)
                        {
                            if (treeFellingGroupLeafCode == null && block.BlockMaterial == EnumBlockMaterial.Leaves && ngcode.Length == treeFellingGroupCode.Length + 1 && ngcode.EndsWith(treeFellingGroupCode))
                            {
                                treeFellingGroupLeafCode = ngcode;
                            }
                            else continue;
                        }
                    }

                    // Only spread from "high to low". i.e. spread from log to leaves, but not from leaves to logs
                    int nspreadIndex = block.Attributes?["treeFellingGroupSpreadIndex"].AsInt(0) ?? 0;
                    if (pos.W < nspreadIndex) continue;

                    checkedPositions.Add(neibPos);

                    if (bh == EnumTreeFellingBehavior.ChopSpreadVertical && !facing.Equals(0, 1, 0) && nspreadIndex > 0) continue;

                    resistance += nspreadIndex + 1;      // leaves -> 1; branchyleaves -> 2; softwood -> 4 etc.
                    if (woodTier == 0) woodTier = nspreadIndex;
                    foundPositions.Push(neibPos.Copy());
                    queue.Enqueue(new Vec4i(neibPos.X, neibPos.Y, neibPos.Z, nspreadIndex));
                }
            }

            return foundPositions;
        }

    }


    public enum EnumTreeFellingBehavior
    {
        NoChop,
        Chop,
        ChopSpreadVertical
    }

    public interface ICustomTreeFellingBehavior
    {
        EnumTreeFellingBehavior GetTreeFellingBehavior(BlockPos pos, Vec3i fromDir, int spreadIndex);
    }
}
