using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace LevelLens.Editor
{
    public interface ILevelLensBatchHandler
    {
        bool CanHandle(ScriptableObject holderAsset);
        IReadOnlyList<string> GetLevelNames(ScriptableObject holderAsset);
        IEnumerator PrepareLevelForScreenshot(ScriptableObject holderAsset, int levelIndex);
    }

    public static class LevelLensBatchHandlerRegistry
    {
        private static readonly List<ILevelLensBatchHandler> _handlers = new List<ILevelLensBatchHandler>();

        public static void Register(ILevelLensBatchHandler handler)
        {
            if (handler != null && !_handlers.Contains(handler))
            {
                _handlers.Add(handler);
            }
        }

        public static ILevelLensBatchHandler FindHandler(ScriptableObject holderAsset)
        {
            if (holderAsset == null) return null;

            // 1. Check registered handlers
            for (int i = 0; i < _handlers.Count; i++)
            {
                if (_handlers[i].CanHandle(holderAsset))
                {
                    return _handlers[i];
                }
            }

            // 2. Discover via reflection across editor assemblies
            var targetType = typeof(ILevelLensBatchHandler);
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                string fullName = assembly.FullName;
                if (fullName.StartsWith("Unity") || fullName.StartsWith("System") || fullName.StartsWith("mscorlib"))
                    continue;

                try
                {
                    foreach (var t in assembly.GetTypes())
                    {
                        if (targetType.IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract)
                        {
                            var instance = (ILevelLensBatchHandler)Activator.CreateInstance(t);
                            Register(instance);
                            if (instance.CanHandle(holderAsset))
                            {
                                return instance;
                            }
                        }
                    }
                }
                catch
                {
                    // Ignore assembly type load exceptions
                }
            }

            return null;
        }
    }
}

