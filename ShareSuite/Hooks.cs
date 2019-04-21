using System.Linq;
using System.Reflection;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;

namespace ShareSuite
{
    public static class Hooks
    {
        static MethodInfo sendPickupMessage =
            typeof(GenericPickupController).GetMethod("SendPickupMessage",
                BindingFlags.NonPublic | BindingFlags.Static);

        public static void SplitTpMoney()
        {
            On.RoR2.TeleporterInteraction.OnInteractionBegin += (orig, self, activator) =>
            {
                if (self.isCharged && ShareSuite.MoneyIsShared.Value)
                {
                    foreach (var player in PlayerCharacterMasterController.instances)
                    {
                        player.master.money = (uint)
                            Mathf.FloorToInt(player.master.money / PlayerCharacterMasterController.instances.Count);
                    }
                }

                orig(self, activator);
            };
        }

        public static void BrittleCrownHook()
        {
            On.RoR2.HealthComponent.TakeDamage += (orig, self, info) =>
            {
                if (!ShareSuite.MoneyIsShared.Value 
                    || !(bool) self.body 
                    || !(bool) self.body.inventory) {
                    orig(self, info);
                    return;
                }
            
                var body = self.body;
                
                var preDamageMoney = self.body.master.money;
                
                orig(self, info);

                if (body.inventory.GetItemCount(ItemIndex.GoldOnHit) <= 0) return;
                foreach (var player in PlayerCharacterMasterController.instances)
                {
                    if (!(bool) player.master.GetBody() || player.master.GetBody() == body) continue;
                    player.master.money -= preDamageMoney - self.body.master.money;
                    EffectManager.instance.SimpleImpactEffect(Resources.Load<GameObject>(
                            "Prefabs/Effects/ImpactEffects/CoinImpact"),
                        player.master.GetBody().corePosition, Vector3.up, true);
                }
            };

            On.RoR2.GlobalEventManager.OnHitEnemy += (orig, self, info, victim) =>
            {
                if (!ShareSuite.MoneyIsShared.Value 
                    || !info.attacker 
                    || !info.attacker.GetComponent<CharacterBody>()) {
                    orig(self, info, victim);
                    return;
                }

                var body = info.attacker.GetComponent<CharacterBody>();
                
                var preDamageMoney = body.master.money;
                
                orig(self, info, victim);
                
                if (!body.inventory) return;

                if (body.inventory.GetItemCount(ItemIndex.GoldOnHit) <= 0) return;
                foreach (var player in PlayerCharacterMasterController.instances)
                {
                    if (!(bool) player.master.GetBody() || player.master.GetBody() == body) continue;
                    player.master.money += body.master.money - preDamageMoney;
                }
            };
        }

        public static void ModifyGoldReward()
        {
            On.RoR2.DeathRewards.OnKilled += (orig, self, info) =>
            {
                orig(self, info);
                if (!ShareSuite.ModIsEnabled.Value
                    || !ShareSuite.MoneyIsShared.Value
                    || !NetworkServer.active) return;

                GiveAllScaledMoney(self.goldReward);
            };

            On.RoR2.BarrelInteraction.OnInteractionBegin += (orig, self, activator) =>
            {
                orig(self, activator);
                if (!ShareSuite.ModIsEnabled.Value
                    || !ShareSuite.MoneyIsShared.Value
                    || !NetworkServer.active) return;

                GiveAllScaledMoney(self.goldReward);
            };
        }

        private static void GiveAllScaledMoney(float goldReward)
        {
            foreach (var player in PlayerCharacterMasterController.instances.Select(p => p.master))
            {
                player.GiveMoney(
                    (uint) Mathf.Floor(goldReward * ShareSuite.MoneyScalar.Value - goldReward));
            }
        }

        public static void DisableInteractablesScaling()
        {
            On.RoR2.SceneDirector.PlaceTeleporter += (orig, self) => //Replace 1 player values
            {
                orig(self);
                FixBoss();
                SyncMoney();
                if (!ShareSuite.ModIsEnabled.Value || !ShareSuite.OverridePlayerScalingEnabled.Value)

                {
                    orig(self);
                    return;
                }

                // Set interactables budget to 200 * config player count (normal calculation)
                Reflection.SetFieldValue(self, "interactableCredit", 200 * ShareSuite.InteractablesCredit.Value);
            };
        }

        private static void SyncMoney()
        {
            if (!ShareSuite.MoneyIsShared.Value) return;
            foreach (var player in PlayerCharacterMasterController.instances)
            {
                player.master.money = NetworkUser.readOnlyInstancesList[0].master.money;
            }
        }

        public static void FixBoss()
        {
            On.RoR2.BossGroup.OnCharacterDeathCallback += (orig, self, report) => {
                IL.RoR2.BossGroup.OnCharacterDeathCallback += il => // Replace boss drops
                {
                    var c = new ILCursor(il).Goto(99);
                    c.Remove();
                    if (ShareSuite.ModIsEnabled.Value && ShareSuite.OverrideBossLootScalingEnabled.Value)
                    {
                        // Needs to reference a getter
                        c.Emit(OpCodes.Ldc_I4, ShareSuite.BossLootCredit.Value);
                    }
                    else
                    {
                        c.Emit(OpCodes.Ldc_I4,
                            // Needs to reference a getter
                            Run.instance.participatingPlayerCount);
                    }
                };
                orig(self, report);
            };
        }


        public static void OnGrantItem()
        {
            On.RoR2.GenericPickupController.GrantItem += (orig, self, body, inventory) =>
            {
                // Item to share
                var item = self.pickupIndex.itemIndex;

                if (!ShareSuite.GetItemBlackList().Contains((int) item)
                    && NetworkServer.active
                    && IsValidPickup(self.pickupIndex)
                    && IsMultiplayer()
                    && ShareSuite.ModIsEnabled.Value)
                    foreach (var player in PlayerCharacterMasterController.instances.Select(p => p.master))
                    {
                        // Ensure character is not original player that picked up item
                        if (player.inventory == inventory) continue;
                        if (player.alive || ShareSuite.DeadPlayersGetItems.Value)
                        {
                            player.inventory.GiveItem(item);

                        //    uint pickupQuantity = 1u;
                        //    if (player.inventory)
                        //    {
                        //        if (item != ItemIndex.None)
                        //        {
                        //            pickupQuantity = (uint)player.inventory.GetItemCount(item);
                        //        }
                        //    }

                        //    var pickmsg = Reflection.GetNestedType<GenericPickupController>("PickupMessage");
                        //    var msg = pickmsg.Instantiate();

                        //    msg.SetFieldValue("masterGameObject", player.gameObject);
                        //    msg.SetFieldValue("pickupIndex", self.pickupIndex);
                        //    msg.SetFieldValue("pickupQuantity", pickupQuantity);
                        }
                    }

                orig(self, body, inventory);
            };
        }

        public static void OnShopPurchase()
        {
            On.RoR2.PurchaseInteraction.OnInteractionBegin += (orig, self, activator) =>
            {
                if (!ShareSuite.ModIsEnabled.Value)
                {
                    orig(self, activator);
                    return;
                }

                // Return if you can't afford the item
                if (!self.CanBeAffordedByInteractor(activator)) return;

                var characterBody = activator.GetComponent<CharacterBody>();
                var inventory = characterBody.inventory;

                if (ShareSuite.MoneyIsShared.Value)
                {
                    //TODO add comments on what this does
                    switch (self.costType)
                    {
                        case CostType.Money:
                        {
                            orig(self, activator);
                            foreach (var playerCharacterMasterController in PlayerCharacterMasterController.instances)
                            {
                                if (playerCharacterMasterController.master.alive &&
                                    playerCharacterMasterController.master.GetBody() != characterBody)
                                {
                                    playerCharacterMasterController.master.money -= (uint) self.cost;
                                }
                            }

                            return;
                        }

                        case CostType.PercentHealth:
                        {
                            orig(self, activator);
                            var teamMaxHealth = 0;
                            foreach (var playerCharacterMasterController in PlayerCharacterMasterController.instances)
                            {
                                var charMaxHealth = playerCharacterMasterController.master.GetBody().maxHealth;
                                if (charMaxHealth > teamMaxHealth)
                                {
                                    teamMaxHealth = (int) charMaxHealth;
                                }
                            }

                            var purchaseInteraction = self.GetComponent<PurchaseInteraction>();
                            var amount = (uint) (teamMaxHealth * purchaseInteraction.cost / 100.0 * 0.5f *
                                                 ShareSuite.MoneyScalar.Value);
                            var purchaseDiff =
                                amount - (uint) ((double) characterBody.maxHealth * purchaseInteraction.cost / 100.0 *
                                                 0.5f);

                            foreach (var playerCharacterMasterController in PlayerCharacterMasterController.instances)
                            {
                                if (!playerCharacterMasterController.master.alive) continue;
                                playerCharacterMasterController.master.GiveMoney(
                                    playerCharacterMasterController.master.GetBody() != characterBody
                                        ? amount
                                        : purchaseDiff);
                            }

                            return;
                        }
                    }
                }

                // If this is not a multi-player server or the fix is disabled, do the normal drop action
                if (!IsMultiplayer() || !ShareSuite.PrinterCauldronFixEnabled.Value)
                {
                    orig(self, activator);
                    return;
                }

                var shop = self.GetComponent<ShopTerminalBehavior>();

                // If the cost type is an item, give the user the item directly and send the pickup message
                if (self.costType == CostType.WhiteItem
                    || self.costType == CostType.GreenItem
                    || self.costType == CostType.RedItem)
                {
                    var item = shop.CurrentPickupIndex().itemIndex;
                    inventory.GiveItem(item);
                    sendPickupMessage.Invoke(null,
                        new object[] {inventory.GetComponent<CharacterMaster>(), shop.CurrentPickupIndex()});
                }

                orig(self, activator);
            };
        }

        public static void OnPurchaseDrop()
        {
            On.RoR2.ShopTerminalBehavior.DropPickup += (orig, self) =>
            {
                if (!ShareSuite.ModIsEnabled.Value)
                {
                    orig(self);
                    return;
                }

                if (!NetworkServer.active) return;
                var costType = self.GetComponent<PurchaseInteraction>().costType;
                Debug.Log("Cost type: " + costType);
                // If this is a multi-player lobby and the fix is enabled and it's not a lunar item, don't drop an item
                if (!IsMultiplayer()
                    || !IsValidPickup(self.CurrentPickupIndex())
                    || !ShareSuite.PrinterCauldronFixEnabled.Value
                    || self.itemTier == ItemTier.Lunar
                    || costType == CostType.Money)
                {
                    // Else drop the item
                    orig(self);
                }
            };
        }

        private static bool IsValidPickup(PickupIndex pickup)
        {
            var item = pickup.itemIndex;
            return IsWhiteItem(item) && ShareSuite.WhiteItemsShared.Value
                   || IsGreenItem(item) && ShareSuite.GreenItemsShared.Value
                   || IsRedItem(item) && ShareSuite.RedItemsShared.Value
                   || pickup.IsLunar() && ShareSuite.LunarItemsShared.Value
                   || IsBossItem(item) && ShareSuite.BossItemsShared.Value
                   || IsQueensGland(item) && ShareSuite.QueensGlandsShared.Value;
        }

        private static bool IsMultiplayer()
        {
            // Check if there are more then 1 players in the lobby
            return PlayerCharacterMasterController.instances.Count > 1;
        }

        public static bool IsWhiteItem(ItemIndex index)
        {
            return ItemCatalog.tier1ItemList.Contains(index);
        }

        public static bool IsGreenItem(ItemIndex index)
        {
            return ItemCatalog.tier2ItemList.Contains(index);
        }

        public static bool IsRedItem(ItemIndex index)
        {
            return ItemCatalog.tier3ItemList.Contains(index);
        }

        public static bool IsBossItem(ItemIndex index)
        {
            return index == ItemIndex.Knurl;
        }

        public static bool IsQueensGland(ItemIndex index)
        {
            return index == ItemIndex.BeetleGland;
        }
    }
}