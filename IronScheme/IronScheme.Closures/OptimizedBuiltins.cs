using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.Scripting;
using Microsoft.Scripting.Actions;
using System.Reflection;

namespace IronScheme.Runtime
{
  public sealed class MultipleValues
  {
    readonly object[] values;

    public MultipleValues(params object[] values)
    {
      this.values = values;
    }

    public object[] ToArray()
    {
      foreach (object item in values)
      {
        if (item is MultipleValues)
        {
          return (object[]) Closure.AssertionViolation(false, "cannot pass multiple values", values);
        }
      }
      return values;
    }

    public object this[int index]
    {
      get { return values[index]; }
      set { values[index] = value; }
    }
  }


  public static class OptimizedBuiltins
  {
#if CPS
    internal static object Call(ICallable c, params object[] args)
    {
      if (c is BuiltinMethod && c != Closure.CPSPrim)
      {
        currentcc = Closure.IdentityForCPS;
        return c.Call(args);
      }
      else
      {
        List<object> newargs = new List<object>();
        newargs.Add(Closure.IdentityForCPS);
        newargs.AddRange(args);
        return c.Call(newargs.ToArray());
      }
    }

    internal static CallTarget1 SymbolValue;

    internal static object CallWithK(ICallable c, ICallable K, params object[] args)
    {
      if (c is BuiltinMethod && c != Closure.CPSPrim)
      {
        object r = null, except = null;
        currentcc = K;
        try
        {
          r = c.Call(args);
        }
        catch (Exception ex)
        {
          except = ex;
        }
        finally
        {
          currentcc = null;
        }

        if (except == null)
        {
          return K.Call(r);
        }
        else
        {
          ICallable raise = SymbolValue(SymbolTable.StringToId("raise")) as ICallable; 
          return CallWithK(raise, K, except);
        }
      }
      else
      {
        List<object> newargs = new List<object>();
        newargs.Add(K);
        newargs.AddRange(args);
        return c.Call(newargs.ToArray());
      }
    }

    internal static CallTarget1 MakeCPS(CallTarget0 prim)
    {
      return delegate(object k)
      {
        return ((ICallable)k).Call(prim());
      };
    }

    internal static CallTarget2 MakeCPS(CallTarget1 prim)
    {
      return delegate(object k, object a1)
      {
        return ((ICallable)k).Call(prim(a1));
      };
    }

    internal static CallTarget3 MakeCPS(CallTarget2 prim)
    {
      return delegate(object k, object a1, object a2)
      {
        return ((ICallable)k).Call(prim(a1,a2));
      };
    }

    internal static CallTargetN MakeCPS(CallTargetN prim)
    {
      return delegate(object[] args)
      {
        ICallable k = args[0] as ICallable;
        List<object> nargs = new List<object>(args);
        nargs.RemoveAt(0);
        return ((ICallable)k).Call(prim(nargs.ToArray()));
      };
    }

    static Dictionary<ICallable, ICallable> cps_cache = new Dictionary<ICallable, ICallable>();
    static Stack<ICallable> current_continuation = new Stack<ICallable>();
    internal static ICallable currentcc;

    internal static ICallable CurrentContinuation
    {
      get
      {
        return currentcc;
        //if (current_continuation.Count == 0)
        //{
        //  return null;
        //}
        //return current_continuation.Peek();
      }
    }

    public static object MakeCPSCallable(object prim)
    {
      ICallable p = prim as ICallable;
      ICallable cpsc;
      if (cps_cache.TryGetValue(p, out cpsc))
      {
        return cpsc;
      }
      else
      {
        CallTarget2 cps = delegate(object k, object args)
        {
          //CallTarget2 cc = delegate(object kk, object ek)
          //{
          //  CallTarget1 before = delegate(object ik)
          //  {
          //    return ((ICallable)ik).Call(Closure.Unspecified);
          //  };

          //  CallTarget1 body = delegate(object ik)
          //  {
          //    current_continuation.Push(ek as ICallable);
          //    object[] aargs = Closure.ArrayFromCons(args);
          //    return CallWithK(p, (ICallable)ik, aargs);
          //  };

          //  CallTarget1 after = delegate(object ik)
          //  {
          //    if (CurrentContinuation != null)
          //    {
          //      current_continuation.Pop();
          //    }
          //    return ((ICallable)ik).Call(Closure.Unspecified);
          //  };

          //  return DynamicWind(k,
          //    Closure.Make(null, before),
          //    Closure.Make(null, body),
          //    Closure.Make(null, after));
            
          //};

          //return CallWithCurrentContinuation(k, Closure.Make(null, cc));
          return CallWithK(p, (ICallable)k, Closure.ArrayFromCons(args));

        };
        return cps_cache[p] = Closure.MakeVarArgX(null, cps, 2);
      }
    }

    public static object DefaultExceptionHandler(object k, object ex)
    {
      throw ex as Exception;
    }

    class Winder
    {
      public ICallable In;
      public ICallable Out;

      public override string ToString()
      {
        return string.Format("In: {0} Out: {1}", In, Out);
      }
    }

    static Stack<Winder> windstack = new Stack<Winder>();

    public static object DynamicWind(object k, object infunc, object bodyfunc, object outfunc)
    {
      ICallable inf = (ICallable)(infunc);
      ICallable bodyf = (ICallable)(bodyfunc);
      ICallable outf = (ICallable)(outfunc);
      ICallable K = (ICallable)(k);

      CallTarget1 k1 = delegate(object V)
      {
        if (windstack.Count == 0)
        {
          Console.WriteLine();
        }
        windstack.Pop();

        CallTarget1 k0 = delegate(object IGNORE)
        {
          object[] args = Closure.ArrayFromCons(V);
          return K.Call(args);
        };

        return CallWithK(outf, Closure.MakeVarArgX(null, k0, 1));
      };

      CallTarget1 k2 = delegate(object IGNORE)
      {
        windstack.Push(new Winder { In = inf, Out = outf });
        return CallWithK(bodyf, Closure.MakeVarArgX(null, k1, 1));
      };

      return CallWithK(inf, Closure.MakeVarArgX(null, k2, 1));

    }

    static void GetWinders(Stack<Winder> now, Stack<Winder> saved, out List<Winder> rewind, out List<Winder> unwind)
    {
      List<Winder> nowl = new List<Winder>(now);
      List<Winder> savedl = new List<Winder>(saved);

      nowl.Reverse();
      savedl.Reverse();

      int i = 0;

      for (; i < nowl.Count && i < savedl.Count; i++)
      {
        if (nowl[i] != savedl[i])
        {
          break;
        }
      }

      unwind = nowl.GetRange(i, nowl.Count - i);
      rewind = savedl.GetRange(i, savedl.Count - i);

      rewind.Reverse();
    }

    //[Builtin("call-with-current-continuation"), Builtin("call/cc")]
    public static object CallWithCurrentContinuation(object k, object fc1)
    {
      ICallable fc = (ICallable)(fc1);
      ICallable e = (ICallable)(k);

      List<Winder> err = new List<Winder>(windstack);
      err.Reverse();
      Stack<Winder> winders = new Stack<Winder>(err);

      CallTarget2 esc = delegate(object ignore, object values)
      {
        object[] args = Closure.ArrayFromCons(values);
        List<Winder> unwind, rewind;

        GetWinders(windstack, winders, out rewind, out unwind);

        ICallable FK = null;

        CallTarget1 fk = delegate(object IGNORE)
        {
          return e.Call(args);
        };

        FK = Closure.Make(null, fk);

        foreach (Winder w in rewind)
        {
          ICallable IFK = FK;
          Winder tw = w;

          CallTarget1 tempk = delegate(object IGNORE)
          {
            windstack.Push(tw);
            return IFK.Call("IGNORE");
          };

          CallTarget1 rk = delegate(object IGNORE)
          {
            return CallWithK(tw.In, Closure.Make(null, tempk));
          };

          FK = Closure.Make(null, rk);
        }

        foreach (Winder w in unwind)
        {
          ICallable IFK = FK;
          Winder tw = w;

          CallTarget1 uk = delegate(object IGNORE)
          {
            windstack.Pop();
            return CallWithK(tw.Out, IFK);
          };

          FK = Closure.Make(null, uk);
        }

        return FK.Call("IGNORE");
      };

      if (fc is BuiltinMethod)
      {
        return e.Call(fc.Call(Closure.MakeVarArgX(null, esc, 2)));
      }
      else
      {
        return fc.Call(e, Closure.MakeVarArgX(null, esc, 2));
      }
    }

    //last arg must be a list
    public static object Apply(object k, object fn, object args)
    {
      ICallable c = (ICallable)(fn);
      object[] targs = Closure.ArrayFromCons(args);

      List<object> allargs = new List<object>();

      int i = 0;

      for (; i < targs.Length - 1; i++)
      {
        allargs.Add(targs[i]);
      }

      allargs.AddRange(Closure.ArrayFromCons(targs[i]));

      return OptimizedBuiltins.CallWithK(c, k as ICallable, allargs.ToArray());
    }

    public static object Values(object k, object list)
    {
      ICallable K = (ICallable)k;
      object[] args = Closure.ArrayFromCons(list);

      if (args.Length == 1)
      {
        return K.Call(args[0]);
      }
      else
      {
        return K.Call(new MultipleValues(args));
      }
      //return K.Call(args);
    }

    //[Builtin("call-with-values")]
    public static object CallWithValues(object k, object producer, object consumer)
    {
      ICallable pro = (ICallable)producer;
      ICallable con = (ICallable)consumer;
      ICallable K = (ICallable)k;

      CallTarget1 ct = delegate(object arg)
      {
        //object[] args = Closure.ArrayFromCons(list);
        if (arg is MultipleValues)
        {
          return CallWithK(con, K, ((MultipleValues)arg).ToArray());
        }
        return CallWithK(con, K, arg);
      };

      return CallWithK(pro, Closure.Make(null, ct));
    }

#else
    //[Builtin("call-with-values")]
    public static object CallWithValues(object producer, object consumer)
    {
      ICallable pro = (ICallable)producer;
      ICallable con = (ICallable)consumer;

      object r = pro.Call();

      if (r is MultipleValues)
      {
        return con.Call(((MultipleValues)r).ToArray());
      }

      return con.Call(r);
    }
#endif


  }
}
