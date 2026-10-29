using System;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using RimWorld;
using Verse;
using Settlement_Services.Framework.Compatibility;

namespace Settlement_Services.Framework.Compat.VanillaSkillsExpanded
{
    internal static class VanillaSkillsExpandedPassionAdapter
    {
        private enum BindingState { Uninitialized, Unavailable, Ready, Incompatible }

        private const string PackageId = "vanillaexpanded.skills";
        private const string AssemblySimpleName = "VSE";
        private const string PassionManagerTypeName = "VSE.Passions.PassionManager";
        private const string PassionToDefMethodName = "PassionToDef";

        private static BindingState state = BindingState.Uninitialized;
        private static bool mappingFailureLogged;

        private static Func<Passion, Def> passionToDef;

        internal static bool IsReady => state == BindingState.Ready;

        internal static readonly IEducationPassionLabelProvider Instance = new AdapterProvider();

        internal static void Initialize()
        {
            if (state != BindingState.Uninitialized) return;

            if (!ModsConfig.IsActive(PackageId))
            {
                state = BindingState.Unavailable;
                return;
            }

            Assembly assembly = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == AssemblySimpleName);
            if (assembly == null) { FailBind("VSE assembly not found"); return; }

            Type managerType = assembly.GetType(PassionManagerTypeName);
            if (managerType == null) { FailBind("PassionManager type not found"); return; }

            MethodInfo method = managerType.GetMethod(PassionToDefMethodName, BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(Passion) }, null);
            if (method == null || !typeof(Def).IsAssignableFrom(method.ReturnType)) { FailBind("PassionToDef(Passion) not found or does not return a Def"); return; }

            try
            {
                ParameterExpression passionParam = Expression.Parameter(typeof(Passion), "passion");
                Expression call = Expression.Convert(Expression.Call(null, method, passionParam), typeof(Def));
                passionToDef = Expression.Lambda<Func<Passion, Def>>(call, passionParam).Compile();
            }
            catch (Exception ex)
            {
                FailBind($"could not bind PassionToDef: {ex.GetType().Name}");
                return;
            }

            state = BindingState.Ready;
            SupportLog.Info("Vanilla Skills Expanded detected; passion labels in education previews use its passion defs.");
        }

        private static bool TryGetLabel(SkillRecord record, out string label)
        {
            label = null;
            if (state != BindingState.Ready || record == null) return false;

            try
            {
                Def def = passionToDef(record.passion);
                if (def == null) return false;

                label = def.LabelCap.ToString();
                return !label.NullOrEmpty();
            }
            catch (Exception ex)
            {
                if (!mappingFailureLogged)
                {
                    mappingFailureLogged = true;
                    SupportLog.Warning($"Vanilla Skills Expanded passion lookup failed ({ex.GetType().Name}); education previews will fall back to the default passion text.");
                }
                label = null;
                return false;
            }
        }

        private sealed class AdapterProvider : IEducationPassionLabelProvider
        {
            public bool TryGetLabel(SkillRecord record, out string label) =>
                VanillaSkillsExpandedPassionAdapter.TryGetLabel(record, out label);
        }

        private static void FailBind(string reason)
        {
            state = BindingState.Incompatible;
            passionToDef = null;
            SupportLog.Warning($"Vanilla Skills Expanded is installed but its API shape doesn't match what this mod expects ({reason}); education previews will show the default passion text.");
        }
    }
}
