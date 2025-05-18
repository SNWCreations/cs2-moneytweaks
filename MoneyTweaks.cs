using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Cvars;
using CounterStrikeSharp.API.Modules.Memory;
using CounterStrikeSharp.API.Modules.Memory.DynamicFunctions;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;

namespace cs2_moneytweaks;

public class MoneyTweaks : BasePlugin
{
    public override string ModuleName => "MoneyTweaks";
    public override string ModuleVersion => "1.0.0";
    public override string ModuleAuthor => "SNWCreations";
    public override string ModuleDescription => "Tweaks & utilities around the CS money system";

    public CCSGameRules? GetGameRules()
    {
        return Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules")
            .FirstOrDefault()?
            .GameRules;
    }

    // EventWarmupEnd is not reliable for tracking warmup status
    // because it would never be fired if the warmup is ended by
    // mp_warmup_end command.
    public bool IsWarmup()
    {
        return GetGameRules()?.WarmupPeriod ?? false;
    }

    private static int GetMaxMoney()
    {
        return ConVar.Find("mp_maxmoney")!.GetPrimitiveValue<int>();
    }

    public static CCSPlayerController? FindPlayerByName(string name)
    {
        return Utilities.GetPlayers().Find(it => name == it.PlayerName);
    }

    public static CCSPlayerController? GetActualTarget(CCSPlayerController? commandCaller, CommandInfo info, int argPos)
    {
        var argNotPresent = info.ArgCount < (argPos - 1);
        if (commandCaller == null && argNotPresent)
        {
            info.ReplyToCommand("Must specify a player at the argument with index " + argPos);
        }
        else
        {
            if (argNotPresent)
            {
                return commandCaller;
            }
            var targetName = info.GetArg(argPos - 1);
            var search = FindPlayerByName(targetName);
            if (search != null)
            {
                return search;
            }
            info.ReplyToCommand("Player not found");
        }
        return null;
    }

    public static void FillMoney(CCSPlayerController controller)
    {
        SetMoney(controller, GetMaxMoney());
    }

    public static void SetMoney(CCSPlayerController controller, int amount)
    {
        if (amount < 0)
        {
            throw new ArgumentException("amount must not be negative");
        }
        controller.InGameMoneyServices!.Account = amount;
        Utilities.SetStateChanged(controller, "CCSPlayerController", "m_pInGameMoneyServices");
    }

    public override void Load(bool hotReload)
    {
        VirtualFunctions.CCSPlayer_ItemServices_CanAcquireFunc.Hook(OnCanAcquire, HookMode.Post);
    }

    public override void Unload(bool hotReload)
    {
        VirtualFunctions.CCSPlayer_ItemServices_CanAcquireFunc.Unhook(OnCanAcquire, HookMode.Post);
    }

    [ConsoleCommand("css_setmoney", "Set the money amount of a player")]
    [RequiresPermissions("@css/cheats")]
    [CommandHelper(minArgs: 1, usage: "<amount> [target]", whoCanExecute: CommandUsage.CLIENT_AND_SERVER)]
    public void OnCmdSetMoney(CCSPlayerController? caller, CommandInfo info)
    {
        if (info.ArgCount > 3)
        {
            info.ReplyToCommand("Too many arguments");
            return;
        }
        if (caller == null && info.ArgCount < 3)
        {
            info.ReplyToCommand("Must specify a player through the second argument");
            return;
        }

        var target = GetActualTarget(caller, info, 3);
        if (target == null)
        {
            return;
        }

        if (int.TryParse(info.GetArg(1), out var amount))
        {
            if (amount >= 0)
            {
                SetMoney(target, amount);
                info.ReplyToCommand("Operation successful");
            }
            else
            {
                info.ReplyToCommand("Amount must not be negative");
            }
        }
        else
        {
            info.ReplyToCommand("Amount must be an integer");
        }
    }
    
    [ConsoleCommand("css_fillmoney", "Fill the player money account")]
    [RequiresPermissions("@css/cheats")]
    [CommandHelper(usage: "[target]", whoCanExecute: CommandUsage.CLIENT_AND_SERVER)]
    public void OnCmdFillMoney(CCSPlayerController? caller, CommandInfo info)
    {
        var target = GetActualTarget(caller, info, 1);
        if (target == null)
        {
            return;
        }
        FillMoney(target);
        info.ReplyToCommand("Operation successful");
    }

    private HookResult OnCanAcquire(DynamicHook hook)
    {
        var pawn = hook.GetParam<CCSPlayer_ItemServices>(0).Pawn.Value.As<CCSPlayerPawn>();
        var acquirer = pawn.Controller.Value?.As<CCSPlayerController>();
        if (acquirer != null && acquirer.IsValid)
        {
            var acquireMethod = hook.GetParam<AcquireMethod>(2);
            if (acquireMethod == AcquireMethod.Buy)
            {
                var result = hook.GetReturn<AcquireResult>();
                if (result == AcquireResult.Allowed)
                {
                    if (IsWarmup())
                    {
                        Logger.LogInformation("Preventing money cost as the game is in warmup period");
                        Server.NextFrame(() => { FillMoney(acquirer); });
                    }
                }
            }
        }
        
        return HookResult.Continue;
    }
}
