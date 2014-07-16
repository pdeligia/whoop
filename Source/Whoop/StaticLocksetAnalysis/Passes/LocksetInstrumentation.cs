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
using Whoop.Regions;

namespace Whoop.SLA
{
  internal class LocksetInstrumentation : ILocksetInstrumentation
  {
    private AnalysisContext AC;

    public LocksetInstrumentation(AnalysisContext ac)
    {
      Contract.Requires(ac != null);
      this.AC = ac;
    }

    public void Run()
    {
      this.AddCurrentLockset();
      this.AddMemoryLocksets();
      this.AddUpdateLocksetFunc();

      this.InstrumentLocksetAnalysisRegions();
      this.InstrumentLoggerSummaryRegions();
      this.InstrumentCheckerSummaryRegions();
    }

    private void AddCurrentLockset()
    {
      this.AC.CurrLockset = new Lockset(new GlobalVariable(Token.NoToken,
        new TypedIdent(Token.NoToken, "CLS",
          new MapType(Token.NoToken, new List<TypeVariable>(),
            new List<Microsoft.Boogie.Type> { this.AC.MemoryModelType },
            Microsoft.Boogie.Type.Bool))));
      this.AC.CurrLockset.Id.AddAttribute("lockset", new object[] { });
      this.AC.Program.TopLevelDeclarations.Add(this.AC.CurrLockset.Id);
    }

    private void AddMemoryLocksets()
    {
      for (int i = 0; i < this.AC.SharedStateAnalyser.MemoryRegions.Count; i++)
      {
        if (RaceInstrumentationUtil.RaceCheckingMethod == RaceCheckingMethod.NORMAL)
        {
          this.AC.Locksets.Add(new Lockset(new GlobalVariable(Token.NoToken,
            new TypedIdent(Token.NoToken, "LS_" + this.AC.SharedStateAnalyser.MemoryRegions[i].Name,
              new MapType(Token.NoToken, new List<TypeVariable>(),
                new List<Microsoft.Boogie.Type> { this.AC.MemoryModelType },
                this.AC.CurrLockset.Id.TypedIdent.Type)))));
        }
        else if (RaceInstrumentationUtil.RaceCheckingMethod == RaceCheckingMethod.WATCHDOG)
        {
          this.AC.Locksets.Add(new Lockset(new GlobalVariable(Token.NoToken,
            new TypedIdent(Token.NoToken, "LS_" + this.AC.SharedStateAnalyser.MemoryRegions[i].Name,
              this.AC.CurrLockset.Id.TypedIdent.Type))));
        }
        Contract.Requires(this.AC.Locksets.Count == i + 1 && this.AC.Locksets[i] != null);

        this.AC.Locksets[i].Id.AddAttribute("lockset", new object[] { });
        this.AC.Program.TopLevelDeclarations.Add(this.AC.Locksets[i].Id);
      }
    }

    private void AddUpdateLocksetFunc()
    {
      List<Variable> inParams = new List<Variable>();
      Variable in1 = new LocalVariable(Token.NoToken, new TypedIdent(Token.NoToken, "lock", this.AC.MemoryModelType));
      Variable in2 = new LocalVariable(Token.NoToken, new TypedIdent(Token.NoToken, "isLocked", Microsoft.Boogie.Type.Bool));
      inParams.Add(in1);
      inParams.Add(in2);

      Procedure proc = new Procedure(Token.NoToken, "_UPDATE_CURRENT_LOCKSET",
                         new List<TypeVariable>(), inParams, new List<Variable>(),
                         new List<Requires>(), new List<IdentifierExpr>(), new List<Ensures>());
      proc.AddAttribute("inline", new object[] { new LiteralExpr(Token.NoToken, BigNum.FromInt(1)) });
      proc.Modifies.Add(new IdentifierExpr(Token.NoToken, this.AC.CurrLockset.Id));

      this.AC.Program.TopLevelDeclarations.Add(proc);
      this.AC.ResContext.AddProcedure(proc);

      Block b = new Block(Token.NoToken, "$bb0", new List<Cmd>(), new ReturnCmd(Token.NoToken));

      List<AssignLhs> newLhss = new List<AssignLhs>();
      List<Expr> newRhss = new List<Expr>();

      newLhss.Add(new MapAssignLhs(this.AC.CurrLockset.Id.tok,
        new SimpleAssignLhs(this.AC.CurrLockset.Id.tok,
          new IdentifierExpr(this.AC.CurrLockset.Id.tok, this.AC.CurrLockset.Id)),
        new List<Expr>(new Expr[] { new IdentifierExpr(in1.tok, in1) })));

      newRhss.Add(new IdentifierExpr(in2.tok, in2));

      AssignCmd assign = new AssignCmd(Token.NoToken, newLhss, newRhss);
      b.Cmds.Add(assign);

      Implementation impl = new Implementation(Token.NoToken, "_UPDATE_CURRENT_LOCKSET",
                              new List<TypeVariable>(), inParams, new List<Variable>(),
                              new List<Variable>(), new List<Block>());
      impl.Blocks.Add(b);
      impl.Proc = proc;
      impl.AddAttribute("inline", new object[] { new LiteralExpr(Token.NoToken, BigNum.FromInt(1)) });

      this.AC.Program.TopLevelDeclarations.Add(impl);
    }

    private void AddLocksetCompFunc(Microsoft.Boogie.Type argType)
    {
      List<Variable> inParams = new List<Variable>();
      Variable lhs = new LocalVariable(Token.NoToken, new TypedIdent(Token.NoToken, "ls$1", argType));
      Variable rhs = new LocalVariable(Token.NoToken, new TypedIdent(Token.NoToken, "ls$2", argType));
      inParams.Add(lhs);
      inParams.Add(rhs);

      Function f = new Function(Token.NoToken, "$cmp_ls", inParams,
                     new LocalVariable(Token.NoToken, new TypedIdent(Token.NoToken, "",
                       Microsoft.Boogie.Type.Bool)));
      f.AddAttribute("inline", Expr.True);
      f.Body = Expr.Neq(this.AC.MakeBVFunctionCall("BV" + lhs.TypedIdent.Type.BvBits + "_AND", "bvand",
        lhs.TypedIdent.Type, new IdentifierExpr(lhs.tok, lhs), new IdentifierExpr(rhs.tok, rhs)),
        new LiteralExpr(Token.NoToken, BigNum.FromInt(0), 1));

      this.AC.Program.TopLevelDeclarations.Add(f);
      this.AC.ResContext.AddProcedure(f);
    }

    private void InstrumentLocksetAnalysisRegions()
    {
      foreach (var region in this.AC.LocksetAnalysisRegions)
      {
        this.InstrumentImplementation(region);
        this.InstrumentProcedure(region);
        this.AddCurrentLocksetInitAssignCmd(region);
      }
    }

    private void InstrumentLoggerSummaryRegions()
    {
      foreach (var region in this.AC.LoggerSummaryRegions)
      {
        this.InstrumentImplementation(region);
        this.InstrumentProcedure(region, true);
      }
    }

    private void InstrumentCheckerSummaryRegions()
    {
      foreach (var region in this.AC.CheckerSummaryRegions)
      {
        this.InstrumentImplementation(region);
        this.InstrumentProcedure(region, true);
      }
    }

    private void InstrumentImplementation(IRegion region)
    {
      foreach (var c in region.Cmds().OfType<CallCmd>())
      {
        if (c.callee.Equals("mutex_lock"))
        {
          c.callee = "_UPDATE_CURRENT_LOCKSET";
          c.Ins.Add(Expr.True);
        }
        else if (c.callee.Equals("mutex_unlock"))
        {
          c.callee = "_UPDATE_CURRENT_LOCKSET";
          c.Ins.Add(Expr.False);
        }
      }
    }

    private void InstrumentProcedure(IRegion region, bool modifiesOnly=false)
    {
      if (region.Procedure().Modifies.Exists(val => val.Name.Equals(this.AC.CurrLockset.Id.Name)))
        return;

      region.Procedure().Modifies.Add(new IdentifierExpr(this.AC.CurrLockset.Id.tok,
        this.AC.CurrLockset.Id));

      if (modifiesOnly)
        return;

      List<Variable> vars = this.AC.SharedStateAnalyser.
        GetAccessedMemoryRegions(region.Implementation());

      Variable dummyPtr = new LocalVariable(Token.NoToken, new TypedIdent(Token.NoToken, "ptr",
        this.AC.MemoryModelType));

      foreach (var ls in this.AC.Locksets)
      {
        foreach (var l in this.AC.Locks)
        {
          Requires require = null;

          if (RaceInstrumentationUtil.RaceCheckingMethod == RaceCheckingMethod.NORMAL)
          {
            require = new Requires(false,
              new ForallExpr(Token.NoToken, new List<Variable> { dummyPtr },
                new NAryExpr(Token.NoToken, new MapSelect(Token.NoToken, 1),
                  new List<Expr>(new Expr[] {
                    new NAryExpr(Token.NoToken, new MapSelect(Token.NoToken, 1),
                      new List<Expr>(new Expr[] {
                        new IdentifierExpr(ls.Id.tok, ls.Id),
                        new IdentifierExpr(dummyPtr.tok, dummyPtr)
                      })),
                    new IdentifierExpr(l.Id.tok, l.Id)
                  })))
            );
          }
          else if (RaceInstrumentationUtil.RaceCheckingMethod == RaceCheckingMethod.WATCHDOG)
          {
            require = new Requires(false,
              new NAryExpr(Token.NoToken, new MapSelect(Token.NoToken, 1),
                new List<Expr>(new Expr[] {
                  new IdentifierExpr(ls.Id.tok, ls.Id),
                  new IdentifierExpr(l.Id.tok, l.Id)
                })));
          }

          region.Procedure().Requires.Add(require);
        }
      }

      foreach (var ls in this.AC.Locksets)
      {
        if (!vars.Any(val => val.Name.Equals(ls.TargetName))) continue;
        region.Procedure().Modifies.Add(new IdentifierExpr(ls.Id.tok, ls.Id));
      }
    }

    private void AddCurrentLocksetInitAssignCmd(LocksetAnalysisRegion region)
    {
      foreach (var l in this.AC.Locks)
      {
        MapAssignLhs lhs =
          new MapAssignLhs(this.AC.CurrLockset.Id.tok,
            new SimpleAssignLhs(this.AC.CurrLockset.Id.tok,
              new IdentifierExpr(this.AC.CurrLockset.Id.tok, this.AC.CurrLockset.Id)),
            new List<Expr>(new Expr[] {
              new IdentifierExpr(l.Id.tok, l.Id)
            }));

        AssignCmd assign = new AssignCmd(Token.NoToken,
                             new List<AssignLhs> { lhs }, new List<Expr> { Expr.False });

        region.Logger().Header().Cmds.Add(assign);
        foreach (var checker in region.Checkers())
          checker.Header().Cmds.Add(assign);
      }
    }
  }
}