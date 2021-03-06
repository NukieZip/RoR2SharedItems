using RoR2;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.Networking;

namespace ShareSuite
{
    public static class ItemSharingHooks
    {
        public static void UnHook()
        {
            On.RoR2.PurchaseInteraction.OnInteractionBegin -= OnShopPurchase;
            On.RoR2.ShopTerminalBehavior.DropPickup -= OnPurchaseDrop;
            On.RoR2.GenericPickupController.GrantItem -= OnGrantItem;
        }
        public static void Hook()
        {
            On.RoR2.PurchaseInteraction.OnInteractionBegin += OnShopPurchase;
            On.RoR2.ShopTerminalBehavior.DropPickup += OnPurchaseDrop;
            On.RoR2.GenericPickupController.GrantItem += OnGrantItem;
        }

        private static void OnGrantItem(On.RoR2.GenericPickupController.orig_GrantItem orig, GenericPickupController self, CharacterBody body, Inventory inventory)
        {
            var item = PickupCatalog.GetPickupDef(self.pickupIndex);
            var itemDef = ItemCatalog.GetItemDef(item.itemIndex);


              if ((ShareSuite.RandomizeSharedPickups.Value || !ShareSuite.GetItemBlackList().Contains((int)item.itemIndex))
                && NetworkServer.active
                && IsValidItemPickup(self.pickupIndex)
                && GeneralHooks.IsMultiplayer())
                foreach (var player in PlayerCharacterMasterController.instances.Select(p => p.master))
                {
                    // Ensure character is not original player that picked up item
                    if (player.inventory == inventory) continue;

                    // Do not reward dead players if not required
                    if (!player.alive && !ShareSuite.DeadPlayersGetItems.Value) continue;

                    if (ShareSuite.RandomizeSharedPickups.Value)
                    {
                        var giveItem = GetRandomItemOfTier(itemDef.tier, item.itemIndex);
                        var givePickupIndex = PickupCatalog.FindPickupIndex(giveItem);
                        player.inventory.GiveItem(giveItem);
                        // Alternative: Only show pickup text for yourself
                        // var givePickupDef = PickupCatalog.GetPickupDef(givePickupIndex);
                        // Chat.AddPickupMessage(body, givePickupDef.nameToken, givePickupDef.baseColor, 1);
                        SendPickupMessage(player, givePickupIndex);
                    }
                    // Otherwise give everyone the same item
                    else
                    {
                        player.inventory.GiveItem(item.itemIndex);
                    }
                }

            orig(self, body, inventory);
        }

        private static void OnPurchaseDrop(On.RoR2.ShopTerminalBehavior.orig_DropPickup orig, ShopTerminalBehavior self)
        {
            if (!NetworkServer.active)
            {
                orig(self);
                return;
            }

            var costType = self.GetComponent<PurchaseInteraction>().costType;

            if (!GeneralHooks.IsMultiplayer() // is not multiplayer
                || !IsValidItemPickup(self.CurrentPickupIndex()) // item is not shared
                || !ShareSuite.PrinterCauldronFixEnabled.Value // dupe fix isn't enabled
                || self.itemTier == ItemTier.Lunar
                || costType == CostTypeIndex.Money)
            {
                orig(self);
            }
        }

        private static void OnShopPurchase(On.RoR2.PurchaseInteraction.orig_OnInteractionBegin orig, PurchaseInteraction self, Interactor activator)
        {
            if (!self.CanBeAffordedByInteractor(activator)) return;

            var characterBody = activator.GetComponent<CharacterBody>();
            var inventory = characterBody.inventory;

            

            #region Cauldronfix

            // If this is not a multi-player server or the fix is disabled, do the normal drop action
            if (!GeneralHooks.IsMultiplayer() || !ShareSuite.PrinterCauldronFixEnabled.Value)
            {
                orig(self, activator);
                return;
            }

            var shop = self.GetComponent<ShopTerminalBehavior>();

            // If the cost type is an item, give the user the item directly and send the pickup message
            if (self.costType == CostTypeIndex.WhiteItem
                || self.costType == CostTypeIndex.GreenItem
                || self.costType == CostTypeIndex.RedItem
                || self.costType == CostTypeIndex.BossItem
                || self.costType == CostTypeIndex.LunarItemOrEquipment)
            {
                var item = PickupCatalog.GetPickupDef(shop.CurrentPickupIndex()).itemIndex;
                inventory.GiveItem(item);
                SendPickupMessage(inventory.GetComponent<CharacterMaster>(), shop.CurrentPickupIndex());
            }

            #endregion Cauldronfix

            orig(self, activator);
        }

        private delegate void SendPickupMessageDelegate(CharacterMaster master, PickupIndex pickupIndex);

        private static readonly SendPickupMessageDelegate SendPickupMessage =
            (SendPickupMessageDelegate)System.Delegate.CreateDelegate(typeof(SendPickupMessageDelegate), typeof(GenericPickupController).GetMethod("SendPickupMessage", BindingFlags.NonPublic | BindingFlags.Static));

        private static bool IsValidItemPickup(PickupIndex pickup)
        {
            var pickupdef = PickupCatalog.GetPickupDef(pickup);
            if (pickupdef.itemIndex != ItemIndex.None)
            {
                var itemdef = ItemCatalog.GetItemDef(pickupdef.itemIndex);
                switch (itemdef.tier)
                {
                    case ItemTier.Tier1:
                        return ShareSuite.WhiteItemsShared.Value;
                    case ItemTier.Tier2:
                        return ShareSuite.GreenItemsShared.Value;
                    case ItemTier.Tier3:
                        return ShareSuite.RedItemsShared.Value;
                    case ItemTier.Lunar:
                        return ShareSuite.LunarItemsShared.Value;
                    case ItemTier.Boss:
                        return ShareSuite.BossItemsShared.Value;
                    default:
                        return false;
                }
            }
            if (pickupdef.equipmentIndex != EquipmentIndex.None)
            {
                // var equipdef = EquipmentCatalog.GetEquipmentDef(pickupdef.equipmentIndex);
                // Optional further checks ...
                return false;
            }
            return false;
        }

        private static ItemIndex GetRandomItemOfTier(ItemTier tier, ItemIndex orDefault)
        {
            switch (tier)
            {
                case ItemTier.Tier1:
                    return PickRandomOf(ItemCatalog.tier1ItemList);
                case ItemTier.Tier2:
                    return PickRandomOf(ItemCatalog.tier2ItemList);
                case ItemTier.Tier3:
                    return PickRandomOf(ItemCatalog.tier3ItemList);
                case ItemTier.Lunar:
                    return PickRandomOf(ItemCatalog.lunarItemList);
                case ItemTier.Boss:
                    return orDefault; // no boss item list, and also probably better anyway
                default:
                    return orDefault;
            }
        }

        private static T PickRandomOf<T>(IList<T> collection) => collection[Random.Range(0, collection.Count)];
    }
}