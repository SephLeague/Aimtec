using System;
using System.Collections.Generic;
using System.Linq;
using Aimtec;
using Aimtec.SDK;
using Aimtec.SDK.Menu;
using Aimtec.SDK.Events;
using Aimtec.SDK.Extensions;
using Aimtec.SDK.Util.Cache;
using Aimtec.SDK.Orbwalking;
using Aimtec.SDK.Menu.Components;
using Aimtec.SDK.Damage;
using Aimtec.SDK.TargetSelector;
using Aimtec.SDK.Prediction.Health;
using SephKayle;

namespace SephKayle
{
    static class Program
    {
        private static Menu Config;
        private static Obj_AI_Hero Player;
        private static float normrange = 150;
        private static float incrange = 525;
        private static Aimtec.SDK.Spell Q, W, E, R, Ignite;
        private static Obj_AI_Hero HealTarget, UltTarget;
        private static float LastHealDetection, LastUltDetection;

        static void Main(string[] args)
        {
            GameEvents.GameStart += OnGameLoad;
        }

        private static void CreateMenu()
        {

            Config = new Menu("SephKayle", "SephKayle", true);

            //OrbWalker
            Orbwalker.Implementation.Attach(Config);

            // Combo Options
            Menu Combo = new Menu("Combo", " Combo");
            Combo.Add(new MenuBool("UseQ", "Use Q"));
            Combo.Add(new MenuBool("UseW", "Use W"));
            Combo.Add(new MenuBool("UseE", "Use E"));
            Combo.Add(new MenuBool("UseR", "Use R"));

            // Harass
            Menu harass = new Menu("Harass", "Harass");
            harass.Add(new MenuKeyBind("Harass.Enabled", "Harass", Aimtec.SDK.Util.KeyCode.H, KeybindType.Toggle, true));
            harass.Add(new MenuList("Harass.Mode", "Harass Mode", new string[] { "Only Mixed", "Always" }, 0));
            harass.Add(new MenuSlider("Harass.Mana", "Min Mana %", 30, 1, 100));
            harass.Add(new MenuBool("Harass.Q", "Use Q"));
            harass.Add(new MenuBool("Harass.E", "Use E"));


            // Waveclear Options
            Menu WaveClear = new Menu("Waveclear", "Waveclear");
            WaveClear.Add(new MenuSlider("WC.Mana", "Min Mana %", 30, 1, 100));
            WaveClear.Add(new MenuBool("UseQwc", "Use Q"));
            WaveClear.Add(new MenuBool("UseEwc", "Use E"));

            // Farm Options
            Menu Farm = new Menu("Farm", "Farm");
            Farm.Add(new MenuBool("UseQfarm", "Use Q"));
            Farm.Add(new MenuBool("UseEfarm", "Use E"));

            // HealManager Options
            Menu HealManager = new Menu("HealManager", "Heal Manager");
            HealManager.Add(new MenuBool("onlyhincdmg", "Only heal if incoming damage", false));
            HealManager.Add(new MenuBool("hdamagedetection", "Disable damage detection", false));
            HealManager.Add(new MenuBool("hcheckdmgafter", "Take HP after damage into consideration"));

            foreach (var hero in ObjectManager.Get<Obj_AI_Hero>().Where(h => h.IsAlly))
            {
                HealManager.Add(new MenuBool("heal" + hero.ChampionName, "Heal " + hero.ChampionName));
                HealManager.Add(new MenuSlider("hpct" + hero.ChampionName, "Health % " + hero.ChampionName, 35, 0, 100));
            }

            // UltimateManager Options
            Menu UltimateManager = new Menu("UltManager", "Ultimate Manager");
            UltimateManager.Add(new MenuBool("onlyuincdmg", "Only ult if incoming damage"));
            UltimateManager.Add(new MenuBool("udamagedetection", "Disable damage detection", false));
            UltimateManager.Add(new MenuBool("ucheckdmgafter", "Take HP after damage into consideration"));
            
            foreach (var hero in ObjectManager.Get<Obj_AI_Hero>().Where(h => h.IsAlly))
            {
                UltimateManager.Add(new MenuBool("ult" + hero.ChampionName, "Ultimate " + hero.ChampionName));
                UltimateManager.Add(new MenuSlider("upct" + hero.ChampionName, "Health % " + hero.ChampionName, 25, 0, 100));
            }

            // Misc Options
            Menu Misc = new Menu("Misc", "Misc");
            Misc.Add(new MenuBool("killsteal", "Killsteal"));
            Misc.Add(new MenuBool("UseElh", "Use E to lasthit"));
            Misc.Add(new MenuBool("Healingon", "Healing On"));
            Misc.Add(new MenuBool("Ultingon", "Ulting On"));
            Misc.Add(new MenuBool("Recallcheck", "Recall check", false));
            Misc.Add(new MenuBool("Debug", "Debug On", false));

            Menu Drawing = new Menu("Drawing", "Drawing");
            Drawing.Add(new MenuBool("disableall", "Disable all"));
            Drawing.Add(new MenuBool("DrawQ", "Draw Q"));
            Drawing.Add(new MenuBool("DrawW", "Draw W"));
            Drawing.Add(new MenuBool("DrawE", "Draw E"));
            Drawing.Add(new MenuBool("DrawR", "Draw R"));

            // Add to Main Menu
            Config.Add(Combo);
            Config.Add(WaveClear);
            Config.Add(Farm);
            Config.Add(harass);
            Config.Add(HealManager);
            Config.Add(UltimateManager);
            Config.Add(Misc);
            Config.Add(Drawing);
            Config.Attach();
        }

        enum HarassMode
        {
            Mixed,
            Always,
            None
        }

        static HarassMode GetHMode()
        {
            if (!GetKeyBind("Harass.Enabled"))
            {
                return HarassMode.None;
            }
            var selindex = Config["Harass.Mode"].Value;
            if (selindex == 0)
            {
                return HarassMode.Mixed;
            }
            else { return HarassMode.Always; }
        }

        private static bool debug()
        {
            return GetBool("Debug");
        }

        static void OnGameLoad()
        {
            Player = ObjectManager.GetLocalPlayer();
            if (Player.UnitSkinName != "Kayle")
            {
                return;
            }

            Console.WriteLine("SephKayle Loaded");
            CreateMenu();
            DefineSpells();
            Game.OnUpdate += GameTick;
            Obj_AI_Base.OnProcessSpellCast += HealUltTrigger;
            Render.OnRender += OnDraw;
        }


        static void OnDraw()
        {
            if (GetBool("disableall"))
            {
                return;
            }

            if (GetBool("DrawQ"))
            {
                Render.Circle(Player.Position, Q.Range, 90, System.Drawing.Color.Aqua);
            }

            if (GetBool("DrawW"))
            {
                Render.Circle(Player.Position, W.Range, 90, System.Drawing.Color.Azure);
            }

            if (GetBool("DrawR"))
            {
                Render.Circle(Player.Position, R.Range, 90, System.Drawing.Color.Red);
            }
        }

        private static void KillSteal()
        {
            var target = GameObjects.AllyHeroes
                .Where(x => !x.IsInvulnerable && x.IsValidTarget(800))
                .OrderBy(x => x.Health).FirstOrDefault();

            if (target != null)
            {
                double igniteDmg = GetIgniteDamage();
                double QDmg = Player.GetSpellDamage(target, SpellSlot.Q);
                var totalksdmg = igniteDmg + QDmg;

                if (target.Health <= QDmg && Player.Distance(target) <= Q.Range)
                {
                    Q.CastOnUnit(target);
                }
                if (target.Health <= igniteDmg && Player.Distance(target) <= Ignite.Range)
                {
                    Player.SpellBook.CastSpell(Ignite.Slot, target);
                }
                if (target.Health <= totalksdmg && Player.Distance(target) <= Q.Range)
                {
                    Q.CastOnUnit(target);
                    Player.SpellBook.CastSpell(Ignite.Slot, target);
                }
            }
        }

        private static bool Eon
        {
            get { return ObjectManager.GetLocalPlayer().AttackRange > 400f; }
        }


        private static bool GetBool(String itemname)
        {
            return Config[itemname].Enabled;
        }

        internal static bool GetKeyBind(string name)
        {
            return Config[name].Enabled;
        }

        private static int Getslider(String itemname)
        {
            return Config[itemname].Value;
        }
        private static float Getsliderf(String itemname)
        {
            return Config[itemname].Value;
        }

        private static void Combo()
        {

            if (GetBool("UseQ") && Q.Ready)
            {
                var qtarget = TargetSelector.GetTarget(Q.Range);
                if (qtarget != null)
                {
                    Q.Cast(qtarget);
                    if (GetBool("UseE") && E.Ready && !Eon)
                    {
                        E.Cast(Player);
                    }
                }
            }

            else
            {
                if (GetBool("UseE") && E.Ready)
                {
                    var etarget = TargetSelector.GetTarget(incrange);
                    if (etarget != null)
                    {
                        var dist = etarget.Distance(Player);
                        if (dist <= incrange * 1.2)
                        {
                            E.Cast(Player);
                        }
                    }
                }
            }
        }

        private static void WaveClear()
        {
            if (Player.ManaPercent() < Getsliderf("WC.Mana"))
            {
                return;
            }

            var minions = ObjectManager.Get<Obj_AI_Minion>().Where(m => m.IsEnemy && Player.Distance(m) <= incrange);
            if (minions.Any() && GetBool("UseEwc") && E.Ready && !Eon)
            {
                E.CastOnUnit(Player);
            }

            if (Config["UseQwc"].Enabled && Q.Ready)
            {

                var allMinions = ObjectManager.Get<Obj_AI_Base>().Where(minion => minion.IsValidSpellTarget(Q.Range));

                var vminions = allMinions.Where(
                   minion =>
                       minion.IsValidTarget() && Player.Distance(minion) >
                            Player.GetFullAttackRange(minion) && Player.Distance(minion) <= Q.Range &&
                       HealthPrediction.Implementation.GetPrediction(minion, (int)((Player.Distance(minion) * 1000) / 1500) + 300 + Game.Ping / 2) <
                       0.75 * Player.GetSpellDamage(minion, SpellSlot.Q));
                var bestminion = vminions.MaxBy(x => x.MaxHealth);
                if (bestminion != null)
                {
                    Orbwalker.Implementation.AttackingEnabled = false;
                    Q.CastOnUnit(bestminion);
                    Orbwalker.Implementation.AttackingEnabled = true;
                }
            }
        }


        static void HealUltTrigger(Obj_AI_Base sender, Obj_AI_BaseMissileClientDataEventArgs args)
        {
            if (GetBool("Recallcheck") && Player.IsRecalling())
            {
                return;
            }

            var target = args.Target as Obj_AI_Hero;
            var senderhero = sender as Obj_AI_Hero;
            var senderturret = sender as Obj_AI_Turret;

            if (sender.IsAlly || (target == null) || !target.IsAlly && !target.IsMe)
            {
                return;
            }

            float setvaluehealth = Getslider("hpct" + target.ChampionName);
            float setvalueult = Getslider("upct" + target.ChampionName);

            bool triggered = false;

            if (W.Ready && GetBool("heal" + target.ChampionName) && (target.HealthPercent() <= setvaluehealth))
            {
                HealTarget = target;
                LastHealDetection = Game.TickCount;
                HealUltManager(true, false, target);
                triggered = true;
            }
            if (R.Ready && GetBool("ult" + target.ChampionName) && (target.HealthPercent() <= setvalueult) && target.Distance(Player) <= R.Range)
            {
                if (args.SpellData.Name.ToLower().Contains("minion") && target.HealthPercent()> 5)
                {
                    return;
                }
                if (debug())
                {
                    Console.WriteLine("Ult target: " + target.ChampionName + " Ult reason: Target hp percent below set value of: " + setvalueult + " Current value is: " + target.HealthPercent()+ " Triggered by: Incoming spell: + " + args.SpellData.Name);
                }
                UltTarget = target;
                LastUltDetection = Game.TickCount;
                HealUltManager(false, true, target);
                triggered = true;

            }

            if (triggered)
            {
                return;
            }

            var unitHero = sender as Obj_AI_Hero;

            var turret = sender as Obj_AI_Turret;

            if (unitHero != null || turret != null)
            {
                var damage = unitHero != null ? unitHero.GetSpellDamage(target, args.SpellSlot) : turret != null ? turret.GetAutoAttackDamage(target) : 0;
                var afterdmg = ((target.Health - damage) / (target.MaxHealth)) * 100f;

                if (W.Ready && Player.Distance(target) <= W.Range && GetBool("heal" + target.ChampionName) && (target.HealthPercent() <= setvaluehealth || (GetBool("hcheckdmgafter") && afterdmg <= setvaluehealth)))
                {
                    if (GetBool("hdamagedetection"))
                    {
                        HealTarget = target;
                        LastHealDetection = Game.TickCount;
                        HealUltManager(true, false, target);
                    }
                }

                if (R.Ready && Player.Distance(target) <= R.Range && GetBool("ult" + target.ChampionName) && (target.HealthPercent() <= setvalueult || (GetBool("ucheckdmgafter") && afterdmg <= setvalueult)) && (senderhero != null || senderturret != null || target.HealthPercent() < 5f))
                {
                    if (GetBool("udamagedetection"))
                    {
                        if (args.SpellData.Name.ToLower().Contains("minion") && target.HealthPercent() > 5)
                        {
                            return;
                        }
                        if (debug())
                        {
                            if (afterdmg <= setvalueult)
                            {
                                Console.WriteLine("Ult target: " + target.ChampionName + " Ult reason: Incoming spell damage will leave us below set value of " + setvalueult + " Current value is: " + target.HealthPercent() + " and after spell health left is: " + afterdmg + " Triggered by: Incoming spell: + " + args.SpellData.Name);
                            }

                            else
                            {
                                Console.WriteLine("Ult target: " + target.ChampionName + " Ult reason: Incoming spell damage and health below set value of " + setvalueult + " Current value is: " + target.HealthPercent() + " Triggered by: Incoming spell: + " + args.SpellData.Name);
                            }
                        }
                        UltTarget = target;
                        LastUltDetection = Game.TickCount;
                        HealUltManager(false, true, target);
                    }
                }
            }
        }


        static void HealUltManager(bool forceheal = false, bool forceult = false, Obj_AI_Hero target = null)
        {
            if (W.Ready) {
                if (Game.TickCount - LastHealDetection < 1000 && HealTarget.IsValidTarget(W.Range, false))
                {
                    var setvaluehealt = Getslider("hpct" + HealTarget.ChampionName);
                    if (HealTarget.Health <= setvaluehealt)
                    {
                        W.Cast(HealTarget);
                    }
                }
            }

            if (R.Ready)
            {
                if (Game.TickCount - LastUltDetection < 1000 && UltTarget.IsValidTarget(R.Range, false))
                {
                    var setvalueult = Getslider("upct" + UltTarget.ChampionName);
                    if (UltTarget.Health <= setvalueult)
                    {
                        R.Cast(UltTarget);
                    }
                }
            }

            if (forceheal && target != null && W.Ready && Player.Distance(target) <= W.Range)
            {
                W.CastOnUnit(target);
                return;
            }
            if (forceult && target != null && R.Ready && Player.Distance(target) <= R.Range)
            {
                if (debug())
                {
                    Console.WriteLine("Forceult");
                }
                R.CastOnUnit(target);
                return;
            }

            if (GetBool("Healingon") && !GetBool("onlyhincdmg"))
            {
                var herolistheal = GameObjects.AllyHeroes
                    .Where(
                        h =>
                            !h.IsDead && GetBool("heal" + h.ChampionName) &&
                            h.HealthPercent()<= Getslider("hpct" + h.ChampionName) && Player.Distance(h) <= R.Range)
                    .OrderByDescending(i => i.IsMe)
                    .ThenBy(i => i.HealthPercent());

                if (W.Ready)
                {
                    if (herolistheal.Contains(Player) && !Player.IsRecalling())
                    {
                        W.CastOnUnit(Player);
                        return;
                    }
                    else if (herolistheal.Any())
                    {
                        var hero = herolistheal.FirstOrDefault();

                        if (Player.Distance(hero) <= R.Range && !Player.IsRecalling() && !hero.IsRecalling())
                        {
                            W.CastOnUnit(hero);
                            return;
                        }
                    }
                }
            }

            if (GetBool("Ultingon") && !GetBool("onlyuincdmg"))
            {
                Console.WriteLine(Player.HealthPercent());
                var herolist = GameObjects.AllyHeroes
                    .Where(
                        h =>
                             !h.IsDead &&
                             GetBool("ult" + h.ChampionName) &&
                            h.HealthPercent()<= Getslider("upct" + h.ChampionName) &&
                            Player.Distance(h) <= R.Range && Player.CountEnemyHeroesInRange(500) > 0).OrderByDescending(i => i.IsMe).ThenBy(i => i.HealthPercent());

                if (R.Ready)
                {
                    if (herolist.Contains(Player))
                    {
                        if (debug())
                        {
                            Console.WriteLine("regultself");
                        }
                        R.CastOnUnit(Player);
                        return;
                    }

                    else if (herolist.Any())
                    {
                        var hero = herolist.FirstOrDefault();

                        if (Player.Distance(hero) <= R.Range)
                        {
                            if (debug())
                            {
                                Console.WriteLine("regultotherorself");
                            }
                            R.CastOnUnit(hero);
                            return;
                        }
                    }
                }
            }
        }

        static void GameTick()
        {
            if (Player.IsDead || GetBool("Recallcheck") && Player.IsRecalling())
            {
                return;
            }

            if (GetHMode() == HarassMode.Always)
            {
                Harass();
            }

            if (!Config["onlyhincdmg"].Enabled|| !Config["onlyuincdmg"].Enabled)
            {
                HealUltManager();
            }

            if (GetBool("killsteal"))
            {
                KillSteal();
            }

            var Orbwalkmode = Orbwalker.Implementation.Mode;
            switch (Orbwalkmode)
            {
                case OrbwalkingMode.Combo:
                    Combo();
                    break;
                case OrbwalkingMode.Laneclear:
                    WaveClear();
                    break;
                case OrbwalkingMode.Mixed:
                    MixedLogic();
                    break;
                case OrbwalkingMode.Lasthit:
                    LHlogic();
                    break;
            }
        }



        private static void LHlogic()
        {
            var allMinions = ObjectManager.Get<Obj_AI_Base>().Where(minion => minion.IsValidSpellTarget(Q.Range));
            var vminions = allMinions.Where(
               minion =>
                   minion.IsValidTarget() && Player.Distance(minion) >
                        Player.GetFullAttackRange(minion) && Player.Distance(minion) <= Q.Range &&
                   HealthPrediction.Implementation.GetPrediction(minion, (int)(Player.Distance(minion) / ObjectManager.GetLocalPlayer().SpellBook.GetSpell(SpellSlot.Q).SpellData.MissileSpeed) + Game.Ping / 2) <
                   0.75 * Player.GetSpellDamage(minion, SpellSlot.Q));

            if (Config["UseQfarm"].Enabled&& Q.Ready)
            {
                var bestminion = vminions.MaxBy(x => x.MaxHealth);
                if (bestminion != null)
                {
                    Orbwalker.Implementation.AttackingEnabled=  false;
                    Q.CastOnUnit(bestminion);
                    Orbwalker.Implementation.AttackingEnabled =  true;
                }
            }


            if (Config["UseEfarm"].Enabled && E.Ready && !Eon)
            {
                var minions = ObjectManager.Get<Obj_AI_Base>().Where(m => m.IsValidTarget(incrange) && HealthPrediction.Implementation.GetPrediction(m, (int)Player.Distance(m) + 300 + Game.Ping / 2) <
                   0.75 * Player.GetAutoAttackDamage(m));
                if (minions.Any())
                {
                    E.CastOnUnit(Player);
                }
            }
            //TODO Better Calculations + More Logic for E activation
        }


        private static void MixedLogic()
        {
            if (GetHMode() == HarassMode.Mixed)
            {
                Harass();
            }

            if (Config["UseEfarm"].Enabled&& E.Ready)
            {
                var minions = ObjectManager.Get<Obj_AI_Base>().Where(m => m.IsValidTarget(incrange) && HealthPrediction.Implementation.GetPrediction(m, (int)(Player.Distance(m)) + 300 + Game.Ping / 2) <
                0.75 * Player.GetAutoAttackDamage(m));
                if (minions.Any() && Config["UseEfarm"].Enabled&& !Eon)
                {
                    E.CastOnUnit(Player);
                }
            }

            if (Config["UseQfarm"].Enabled&& Q.Ready)
            {
                var allMinions = ObjectManager.Get<Obj_AI_Base>().Where(minion => minion.IsValidSpellTarget(Q.Range));
                var vminions = allMinions.Where(
                   minion =>
                       minion.IsValidTarget() && Player.Distance(minion) >
                            Player.GetFullAttackRange(minion) && Player.Distance(minion) <= Q.Range &&
                       HealthPrediction.Implementation.GetPrediction(minion, (int)(Player.Distance(minion) / ObjectManager.GetLocalPlayer().SpellBook.GetSpell(SpellSlot.Q).SpellData.MissileSpeed) + Game.Ping / 2) <
                       0.75 * Player.GetSpellDamage(minion, SpellSlot.Q));

                var bestminion = vminions.MaxBy(x => x.MaxHealth);
                if (bestminion != null)
                {
                    Orbwalker.Implementation.AttackingEnabled =  false;
                    Q.CastOnUnit(bestminion);
                    Orbwalker.Implementation.AttackingEnabled =  true;
                    return;
                }
            }

            //TODO Better Calculations + More Logic for E activation
        }

        private static void Harass()
        {
            if (Player.ManaPercent() < Getslider("Harass.Mana"))
            {
                return;
            }
            if (GetBool("Harass.Q"))
            {
                var Targ = TargetSelector.GetTarget(Q.Range);
                if (Targ != null && Q.Ready && Player.Distance(Targ) <= Q.Range)
                {
                    Q.Cast(Targ);
                }
            }

            if (GetBool("Harass.E"))
            {
                var Targ = TargetSelector.GetTarget(incrange);
                if (Targ != null && E.Ready && Player.Distance(Targ) > normrange)
                {
                    E.Cast(Player);
                }
            }
        }


        static void DefineSpells()
        {
            Q = new Aimtec.SDK.Spell(SpellSlot.Q, 650);
            W = new Aimtec.SDK.Spell(SpellSlot.W, 900);
            E = new Aimtec.SDK.Spell(SpellSlot.E, 0);
            R = new Aimtec.SDK.Spell(SpellSlot.R, 900);

            var ignite = ObjectManager.GetLocalPlayer().SpellBook.GetSpell(ObjectManager.GetLocalPlayer().GetSpellSlot("summonerdot"));

            if (ignite.Slot != SpellSlot.Unknown)
            {
                Ignite = new Aimtec.SDK.Spell(ignite.Slot, 600);
            }
        }

        static float GetIgniteDamage()
        {
            return 50 + (Player.Level * 20);
        }
    }
}
