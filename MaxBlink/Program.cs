// <copyright file="Program.cs" company="Ensage">
//     Copyright (c) 2019 Ensage.
// </copyright>

namespace MaxBlink
{
    using System;
    using System.ComponentModel;
    using System.ComponentModel.Composition;
    using System.Linq;
    using System.Windows.Input;

    using Ensage;
    using Ensage.SDK.Extensions;
    using Ensage.SDK.Geometry;
    using Ensage.SDK.Helpers;
    using Ensage.SDK.Menu;
    using Ensage.SDK.Menu.Attributes;
    using Ensage.SDK.Menu.Items;
    using Ensage.SDK.Renderer.Particle;
    using Ensage.SDK.Service;
    using Ensage.SDK.Service.Metadata;

    using NLog;

    using SharpDX;

    [Menu("MaxBlink")]
    [ExportPlugin("MaxBlink")]
    public class Program : Plugin
    {
        private static readonly Logger Log = LogManager.GetCurrentClassLogger();

        private readonly IServiceContext context;

        private readonly Lazy<MenuManager> menuManager;
        private readonly Lazy<IParticleManager> particleManager;

        private bool autoMode = true;

        private float blinkRange = 1200.0f - 1.0f;

        [ImportingConstructor]
        public Program([Import] IServiceContext context, [Import] Lazy<MenuManager> menuManager, [Import] Lazy<IParticleManager> particleManager)
        {
            this.context = context;
            this.menuManager = menuManager;
            this.particleManager = particleManager;
        }

        [Item("Automode")]
        [PermaShow]
        [Tooltip("Automatically adjusts blink range to maximum")]
        [DefaultValue(true)]
        public bool AutoMode
        {
            get
            {
                return autoMode;
            }
            set
            {
                autoMode = value;
                if (value)
                {
                    Player.OnExecuteOrder += Player_OnExecuteOrder;
                }
                else
                {
                    Player.OnExecuteOrder -= Player_OnExecuteOrder;
                }
            }
        }

        [Item("Range Display")]
        [Tooltip("Shows the range of the blink dagger")]
        [DefaultValue(true)]
        public bool BlinkRangeDisplay { get; set; }

        private bool blinkParticleEffect = false;

        [Item("Hotkey")]
        [Tooltip("Uses blink to mouse cursor position")]
        public HotkeySelector Hotkey { get; set; }

        protected override void OnActivate()
        {
            try
            {
                Hotkey = new HotkeySelector(Key.None, OnHotkeyPressed);
                menuManager.Value.RegisterMenu(this);

                var data = Ability.GetAbilityDataById(AbilityId.item_blink);
                if (data != null)
                {
                    var entry = data.AbilitySpecialData.FirstOrDefault(x => x.Name == "blink_range");
                    if (entry != null)
                    {
                        blinkRange = entry.Value - 1.0f;
                    }
                    else
                    {
                        Log.Info("can't find blink_range entry");
                    }
                }
                else
                {
                    Log.Info($"can't find ability data for {AbilityId.item_blink}");
                }

                UpdateManager.Subscribe(OnUpdate, 500);
            }
            catch (Exception e)
            {
                Log.Error(e);
            }
        }

        protected override void OnDeactivate()
        {
            try
            {
                menuManager.Value.DeregisterMenu(this);
                Player.OnExecuteOrder -= Player_OnExecuteOrder;
                UpdateManager.Unsubscribe(OnUpdate);

                if (blinkParticleEffect)
                {
                    particleManager.Value.Remove($"blink_range_{context.Owner.Handle.Index}");
                    blinkParticleEffect = false;
                }
            }
            catch (Exception e)
            {
                Log.Error(e);
            }
        }

        private void OnHotkeyPressed(MenuInputEventArgs args)
        {
            try
            {
                var player = ObjectManager.LocalPlayer;
                if (player != null)
                {
                    var selection = player.Selection.FirstOrDefault(
                        x => x.IsAlive
                            && x is Unit unit
                            && !unit.IsIllusion
                            && unit.IsControllable
                            && unit.HasInventory
                            && unit.Inventory.Items.Any(y => y.Id == AbilityId.item_blink)) as Unit;
                    if (selection != null)
                    {
                        UseBlinkOnMousePosition(selection);
                        return;
                    }
                }

                UseBlinkOnMousePosition(context.Owner);
            }
            catch (Exception e)
            {
                Log.Error(e);
            }
        }

        private void OnUpdate()
        {
            try
            {
                var hasBlink = context.Owner.Inventory.Items.Any(x => x.Id == AbilityId.item_blink);
                if (BlinkRangeDisplay && hasBlink)
                {
                    if (!blinkParticleEffect)
                    {
                        particleManager.Value.DrawRange(context.Owner, $"blink_range_{context.Owner.Handle.Index}", blinkRange + 8.0f, Color.Blue);
                        blinkParticleEffect = true;
                    }
                }
                else
                {
                    if (blinkParticleEffect)
                    {
                        particleManager.Value.Remove($"blink_range_{context.Owner.Handle.Index}");
                        blinkParticleEffect = false;
                    }
                }
            }
            catch (Exception e)
            {
                Log.Error(e);
            }
        }

        private void Player_OnExecuteOrder(Player sender, ExecuteOrderEventArgs args)
        {
            try
            {
                if (!args.IsPlayerInput)
                {
                    return;
                }

                if (args.OrderId != OrderId.AbilityLocation)
                {
                    return;
                }

                var item = args.Ability as Item;
                if (item == null || item.Id != AbilityId.item_blink)
                {
                    return;
                }

                var owner = item.Owner as Unit;
                if (owner == null)
                {
                    return;
                }

                var ownerPosition = owner.NetworkPosition;
                var distance = args.TargetPosition.Distance2D(ownerPosition);
                if (distance <= blinkRange)
                {
                    return;
                }

                var targetPosition = ownerPosition.Extend(args.TargetPosition, blinkRange);
                Player.UseAbility(owner, item, targetPosition);
                args.Process = false;
            }
            catch (Exception e)
            {
                Log.Error(e);
            }
        }

        private void UseBlinkOnMousePosition(Unit unit)
        {
            if (!unit.IsValid || !unit.IsAlive)
            {
                return;
            }

            if (unit.IsRooted() || unit.IsStunned() || unit.IsHexed() || unit.IsMuted() || unit.UnitState.HasFlag(UnitState.Tethered))
            {
                return;
            }

            var blink = unit.Inventory.Items.FirstOrDefault(x => x.Id == AbilityId.item_blink);
            if (blink == null)
            {
                return;
            }

            if (blink.Cooldown > 0)
            {
                return;
            }

            var unitPosition = unit.NetworkPosition;
            var targetPosition = Game.MousePosition;

            var distance = unitPosition.Distance2D(targetPosition);
            if (distance >= blinkRange)
            {
                targetPosition = unitPosition.Extend(targetPosition, blinkRange);
            }

            blink.UseAbility(targetPosition);
        }
    }
}