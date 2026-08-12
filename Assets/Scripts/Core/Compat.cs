using System;
using System.Collections.Generic;

namespace FallenAngel.Compat
{
    /// <summary>
    /// ObjectPool 兼容层：
    /// Unity 2021+ 自带 UnityEngine.Pool.ObjectPool（官方）
    /// 低版本 Unity 则回退到本类实现，保证编译通过
    /// </summary>
    public class SimpleObjectPool<T> : IDisposable where T : class
    {
        private readonly Stack<T> stack;
        private readonly Func<T> createFunc;
        private readonly Action<T> actionOnGet;
        private readonly Action<T> actionOnRelease;
        private readonly Action<T> actionOnDestroy;
        private readonly int maxSize;
        private int countAll;

        public int CountInactive => stack.Count;

        public SimpleObjectPool(Func<T> createFunc, Action<T> actionOnGet = null,
            Action<T> actionOnRelease = null, Action<T> actionOnDestroy = null,
            int defaultCapacity = 10, int maxSize = 10000)
        {
            if (createFunc == null) throw new ArgumentNullException(nameof(createFunc));
            this.createFunc = createFunc;
            this.actionOnGet = actionOnGet;
            this.actionOnRelease = actionOnRelease;
            this.actionOnDestroy = actionOnDestroy;
            this.maxSize = maxSize;
            stack = new Stack<T>(defaultCapacity);
        }

        public T Get()
        {
            T item;
            if (stack.Count == 0)
            {
                item = createFunc();
                countAll++;
            }
            else
            {
                item = stack.Pop();
            }
            actionOnGet?.Invoke(item);
            return item;
        }

        public void Release(T item)
        {
            if (item == null) return;
            if (stack.Count < maxSize)
            {
                actionOnRelease?.Invoke(item);
                stack.Push(item);
            }
            else
            {
                actionOnDestroy?.Invoke(item);
            }
        }

        public void Dispose()
        {
            if (actionOnDestroy != null)
            {
                foreach (var item in stack)
                    actionOnDestroy(item);
            }
            stack.Clear();
            countAll = 0;
        }
    }
}
