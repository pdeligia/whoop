﻿// ===-----------------------------------------------------------------------==//
//
//                 Whoop - a Verifier for Device Drivers
//
//  Copyright (c) 2013-2014 Pantazis Deligiannis (p.deligiannis@imperial.ac.uk)
//
//  This file is distributed under the Microsoft Public License.  See
//  LICENSE.TXT for details.
//
// ===----------------------------------------------------------------------===//

using System;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.Linq;

using Microsoft.Boogie;
using Microsoft.Basetypes;

using Whoop.Analysis;
using Whoop.Domain.Drivers;
using Whoop.Regions;
using System.Diagnostics.SymbolStore;

namespace Whoop.Instrumentation
{
  internal class RaceInstrumentation : IPass
  {
    private AnalysisContext AC;
    private EntryPoint EP;
    private ExecutionTimer Timer;

    public RaceInstrumentation(AnalysisContext ac, EntryPoint ep)
    {
      Contract.Requires(ac != null && ep != null);
      this.AC = ac;
      this.EP = ep;
    }

    public void Run()
    {
      if (WhoopCommandLineOptions.Get().MeasurePassExecutionTime)
      {
        this.Timer = new ExecutionTimer();
        this.Timer.Start();
      }

      this.AddAccessFuncs(AccessType.WRITE);
      this.AddAccessFuncs(AccessType.READ);

      foreach (var region in this.AC.InstrumentationRegions)
      {
        this.InstrumentImplementation(region);
      }

      foreach (var region in this.AC.InstrumentationRegions)
      {
        this.InstrumentProcedure(region);
      }

      if (WhoopCommandLineOptions.Get().MeasurePassExecutionTime)
      {
        this.Timer.Stop();
        Console.WriteLine(" |  |------ [RaceInstrumentation] {0}", this.Timer.Result());
      }
    }

    #region race checking verification variables and methods

    private void AddAccessFuncs(AccessType access)
    {
      foreach (var mr in SharedStateAnalyser.GetMemoryRegions(this.EP))
      {
        List<Variable> inParams = new List<Variable>();

        if (mr.TypedIdent.Type.IsMap)
        {
          inParams.Add(new LocalVariable(Token.NoToken, new TypedIdent(Token.NoToken, "ptr",
            this.AC.MemoryModelType)));
        }

        Procedure proc = new Procedure(Token.NoToken, this.MakeAccessFuncName(access, mr.Name),
          new List<TypeVariable>(), inParams, new List<Variable>(),
          new List<Requires>(), new List<IdentifierExpr>(), new List<Ensures>());
        proc.AddAttribute("inline", new object[] { new LiteralExpr(Token.NoToken, BigNum.FromInt(1)) });

        this.AC.TopLevelDeclarations.Add(proc);
        this.AC.ResContext.AddProcedure(proc);

        var cmds = new List<Cmd>();

        foreach (var ls in this.AC.MemoryLocksets)
        {
          if (!ls.TargetName.Equals(mr.Name))
            continue;
          if (this.ShouldSkipLockset(ls))
            continue;

          foreach (var cls in this.AC.CurrentLocksets)
          {
            if (!cls.Lock.Name.Equals(ls.Lock.Name))
              continue;

            IdentifierExpr lsExpr = new IdentifierExpr(ls.Id.tok, ls.Id);

            cmds.Add(new AssignCmd(Token.NoToken,
              new List<AssignLhs>() {
              new SimpleAssignLhs(Token.NoToken, lsExpr)
            }, new List<Expr> {
              Expr.And(new IdentifierExpr(cls.Id.tok, cls.Id), lsExpr)
            }));

            proc.Modifies.Add(lsExpr);
            break;
          }
        }

        if (access == AccessType.WRITE)
        {
          foreach (var acv in this.AC.GetWriteAccessCheckingVariables())
          {
            if (!acv.Name.Split('_')[1].Equals(mr.Name))
              continue;

            var wacs = this.AC.GetWriteAccessCheckingVariables().Find(val =>
              val.Name.Contains(this.AC.GetWriteAccessVariableName(this.EP, mr.Name)));
            var wacsExpr = new IdentifierExpr(wacs.tok, wacs);

            cmds.Add(new AssignCmd(Token.NoToken,
              new List<AssignLhs>() {
              new SimpleAssignLhs(Token.NoToken, wacsExpr)
            }, new List<Expr> {
              Expr.True
            }));

            proc.Modifies.Add(wacsExpr);
          }
        }
        else if (access == AccessType.READ)
        {
          foreach (var acv in this.AC.GetReadAccessCheckingVariables())
          {
            if (!acv.Name.Split('_')[1].Equals(mr.Name))
              continue;

            var racs = this.AC.GetReadAccessCheckingVariables().Find(val =>
              val.Name.Contains(this.AC.GetReadAccessVariableName(this.EP, mr.Name)));
            var racsExpr = new IdentifierExpr(racs.tok, racs);

            cmds.Add(new AssignCmd(Token.NoToken,
              new List<AssignLhs>() {
              new SimpleAssignLhs(Token.NoToken, racsExpr)
            }, new List<Expr> {
              Expr.True
            }));

            proc.Modifies.Add(racsExpr);
          }
        }

        List<BigBlock> blocks = null;
        if (mr.TypedIdent.Type.IsMap)
        {
          var ptr = new LocalVariable(Token.NoToken, new TypedIdent(Token.NoToken, "ptr",
            this.AC.MemoryModelType));
          var watchdog = this.AC.GetAccessWatchdogConstants().Find(val =>
            val.Name.Contains(this.AC.GetAccessWatchdogConstantName(mr.Name)));

          var ptrExpr = new IdentifierExpr(ptr.tok, ptr);
          var watchdogExpr = new IdentifierExpr(watchdog.tok, watchdog);
          var guardExpr = Expr.Eq(watchdogExpr, ptrExpr);

          var ifStmts = new StmtList(new List<BigBlock> {
            new BigBlock(Token.NoToken, null, cmds, null, null) }, Token.NoToken);
          var ifCmd = new IfCmd(Token.NoToken, guardExpr, ifStmts, null, null);

          blocks = new List<BigBlock> {
            new BigBlock(Token.NoToken, "_" + access.ToString(), new List<Cmd>(), ifCmd, null) };
        }
        else
        {
          blocks = new List<BigBlock> {
            new BigBlock(Token.NoToken, "_" + access.ToString(), cmds, null, null) };
        }

        Implementation impl = new Implementation(Token.NoToken, this.MakeAccessFuncName(access, mr.Name),
          new List<TypeVariable>(), inParams, new List<Variable>(), new List<Variable>(),
          new StmtList(blocks, Token.NoToken));

        impl.Proc = proc;
        impl.AddAttribute("inline", new object[] { new LiteralExpr(Token.NoToken, BigNum.FromInt(1)) });

        this.AC.TopLevelDeclarations.Add(impl);
      }
    }

    #endregion

    #region race checking instrumentation

    private void InstrumentImplementation(InstrumentationRegion region)
    {
      foreach (var block in region.Blocks())
      {
        for (int idx = 0; idx < block.Cmds.Count; idx++)
        {
          if (!(block.Cmds[idx] is AssignCmd)) continue;
          var assign = block.Cmds[idx] as AssignCmd;

          var lhssMap = assign.Lhss.OfType<MapAssignLhs>();
          var lhss = assign.Lhss.OfType<SimpleAssignLhs>();
          var rhssMap = assign.Rhss.OfType<NAryExpr>();
          var rhss = assign.Rhss.OfType<IdentifierExpr>();

          CallCmd call = null;
          if (lhssMap.Count() == 1)
          {
            var lhs = lhssMap.First();
            if (lhs.DeepAssignedIdentifier.Name.StartsWith("$M.") &&
              lhs.Map is SimpleAssignLhs && lhs.Indexes.Count == 1)
            {
              if (SharedStateAnalyser.GetMemoryRegions(this.EP).Any(val =>
                val.Name.Equals(lhs.DeepAssignedIdentifier.Name)))
              {
                var ind = lhs.Indexes[0];
                call = new CallCmd(Token.NoToken,
                  this.MakeAccessFuncName(AccessType.WRITE, lhs.DeepAssignedIdentifier.Name),
                  new List<Expr> { ind }, new List<IdentifierExpr>());

                if (rhssMap.Count() == 0 && assign.Rhss.Count == 1 &&
                  assign.Rhss[0].ToString().StartsWith("$p"))
                {
                  call.Attributes = new QKeyValue(Token.NoToken, "rhs",
                    new List<object>() { assign.Rhss[0]
                  }, call.Attributes);
                }

                if (!region.HasWriteAccess.ContainsKey(lhs.DeepAssignedIdentifier.Name))
                  region.HasWriteAccess.Add(lhs.DeepAssignedIdentifier.Name, 0);
                region.HasWriteAccess[lhs.DeepAssignedIdentifier.Name] = 
                  region.HasWriteAccess[lhs.DeepAssignedIdentifier.Name] + 1;
              }
              else
              {
                call = new CallCmd(Token.NoToken, "_NO_OP_$" + this.EP.Name,
                  new List<Expr>(), new List<IdentifierExpr>());
              }
            }
          }
          else if (lhss.Count() == 1)
          {
            var lhs = lhss.First();
            if (lhs.DeepAssignedIdentifier.Name.StartsWith("$M."))
            {
              if (SharedStateAnalyser.GetMemoryRegions(this.EP).Any(val =>
                val.Name.Equals(lhs.DeepAssignedIdentifier.Name)))
              {
                call = new CallCmd(Token.NoToken,
                  this.MakeAccessFuncName(AccessType.WRITE, lhs.DeepAssignedIdentifier.Name),
                  new List<Expr>(), new List<IdentifierExpr>());

                if (rhssMap.Count() == 0 && assign.Rhss.Count == 1 &&
                  assign.Rhss[0].ToString().StartsWith("$p"))
                {
                  call.Attributes = new QKeyValue(Token.NoToken, "rhs",
                    new List<object>() { assign.Rhss[0]
                  }, call.Attributes);
                }

                if (!region.HasWriteAccess.ContainsKey(lhs.DeepAssignedIdentifier.Name))
                  region.HasWriteAccess.Add(lhs.DeepAssignedIdentifier.Name, 0);
                region.HasWriteAccess[lhs.DeepAssignedIdentifier.Name] = 
                  region.HasWriteAccess[lhs.DeepAssignedIdentifier.Name] + 1;
              }
              else
              {
                call = new CallCmd(Token.NoToken, "_NO_OP_$" + this.EP.Name,
                  new List<Expr>(), new List<IdentifierExpr>());
              }
            }
          }

          if (rhssMap.Count() == 1)
          {
            var rhs = rhssMap.First();
            if (rhs.Fun is MapSelect && rhs.Args.Count == 2 &&
              (rhs.Args[0] as IdentifierExpr).Name.StartsWith("$M."))
            {
              if (SharedStateAnalyser.GetMemoryRegions(this.EP).Any(val =>
                val.Name.Equals((rhs.Args[0] as IdentifierExpr).Name)))
              {
                call = new CallCmd(Token.NoToken,
                  this.MakeAccessFuncName(AccessType.READ, (rhs.Args[0] as IdentifierExpr).Name),
                  new List<Expr> { rhs.Args[1] }, new List<IdentifierExpr>());

                if (!region.HasReadAccess.ContainsKey((rhs.Args[0] as IdentifierExpr).Name))
                  region.HasReadAccess.Add((rhs.Args[0] as IdentifierExpr).Name, 0);
                region.HasReadAccess[(rhs.Args[0] as IdentifierExpr).Name] = 
                  region.HasReadAccess[(rhs.Args[0] as IdentifierExpr).Name] + 1;
              }
              else
              {
                call = new CallCmd(Token.NoToken, "_NO_OP_$" + this.EP.Name,
                  new List<Expr>(), new List<IdentifierExpr>());
              }
            }
          }
          else if (rhss.Count() == 1)
          {
            var rhs = rhss.First();
            if (rhs.Name.StartsWith("$M."))
            {
              if (SharedStateAnalyser.GetMemoryRegions(this.EP).Any(val =>
                val.Name.Equals(rhs.Name)))
              {
                call = new CallCmd(Token.NoToken,
                  this.MakeAccessFuncName(AccessType.READ, rhs.Name),
                  new List<Expr>(), new List<IdentifierExpr>());

                if (!region.HasReadAccess.ContainsKey(rhs.Name))
                  region.HasReadAccess.Add(rhs.Name, 0);
                region.HasReadAccess[rhs.Name] = region.HasReadAccess[rhs.Name] + 1;
              }
              else
              {
                call = new CallCmd(Token.NoToken, "_NO_OP_$" + this.EP.Name,
                  new List<Expr>(), new List<IdentifierExpr>());
              }
            }
          }

          if (call != null)
            block.Cmds.Insert(idx + 1, call);
        }
      }

      foreach (var write in region.HasWriteAccess)
      {
        if (!this.EP.HasWriteAccess.ContainsKey(write.Key))
          this.EP.HasWriteAccess.Add(write.Key, 0);
        this.EP.HasWriteAccess[write.Key] = this.EP.HasWriteAccess[write.Key] + write.Value;
      }

      foreach (var read in region.HasReadAccess)
      {
        if (!this.EP.HasReadAccess.ContainsKey(read.Key))
          this.EP.HasReadAccess.Add(read.Key, 0);
        this.EP.HasReadAccess[read.Key] = this.EP.HasReadAccess[read.Key] + read.Value;
      }
    }

    private void InstrumentProcedure(InstrumentationRegion region)
    {
      var vars = SharedStateAnalyser.GetMemoryRegions(this.EP);

      foreach (var acv in this.AC.GetWriteAccessCheckingVariables())
      {
        string targetName = acv.Name.Split('_')[1];
        if (!vars.Any(val => val.Name.Equals(targetName)))
          continue;
        if (!this.EP.HasWriteAccess.ContainsKey(targetName))
          continue;

        if (!region.Procedure().Modifies.Any(mod => mod.Name.Equals(acv.Name)))
          region.Procedure().Modifies.Add(new IdentifierExpr(acv.tok, acv));
      }

      foreach (var acv in this.AC.GetReadAccessCheckingVariables())
      {
        string targetName = acv.Name.Split('_')[1];
        if (!vars.Any(val => val.Name.Equals(targetName)))
          continue;
        if (!this.EP.HasReadAccess.ContainsKey(targetName))
          continue;

        if (!region.Procedure().Modifies.Any(mod => mod.Name.Equals(acv.Name)))
          region.Procedure().Modifies.Add(new IdentifierExpr(acv.tok, acv));
      }
    }

    #endregion

    #region helper functions

    private string MakeAccessFuncName(AccessType access, string name)
    {
      return "_" + access.ToString() + "_LS_" + name + "_$" + this.EP.Name;
    }

    private bool ShouldSkipLockset(Lockset ls)
    {
      if (ls.Lock.Name.Equals("lock$power") && !this.EP.IsCallingPowerLock)
        return true;
      else if (ls.Lock.Name.Equals("lock$rtnl") && !this.EP.IsCallingRtnlLock)
        return true;
      else if (ls.Lock.Name.Equals("lock$tx") && !this.EP.IsCallingTxLock)
        return true;
      return false;
    }

    #endregion
  }
}
