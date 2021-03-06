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

namespace Whoop.Analysis
{
  internal class SharedStateAbstraction : IPass
  {
    private AnalysisContext AC;
    private ExecutionTimer Timer;

    public SharedStateAbstraction(AnalysisContext ac)
    {
      Contract.Requires(ac != null);
      this.AC = ac;
    }

    public void Run()
    {
      if (WhoopCommandLineOptions.Get().MeasurePassExecutionTime)
      {
        this.Timer = new ExecutionTimer();
        this.Timer.Start();
      }

      foreach (var region in this.AC.InstrumentationRegions)
      {
        this.AbstractReadAccesses(region);
        this.AbstractWriteAccesses(region);
        this.CleanUpModset(region);
      }

      if (WhoopCommandLineOptions.Get().MeasurePassExecutionTime)
      {
        this.Timer.Stop();
        Console.WriteLine(" |  |------ [SharedStateAbstraction] {0}", this.Timer.Result());
      }
    }

    private void AbstractReadAccesses(InstrumentationRegion region)
    {
      foreach (var b in region.Blocks())
      {
        for (int k = 0; k < b.Cmds.Count; k++)
        {
          if (!(b.Cmds[k] is AssignCmd)) continue;

          foreach (var rhs in (b.Cmds[k] as AssignCmd).Rhss.OfType<NAryExpr>())
          {
            if (!(rhs.Fun is MapSelect) || rhs.Args.Count != 2 ||
              !((rhs.Args[0] as IdentifierExpr).Name.StartsWith("$M.")))
              continue;

            Variable v = (b.Cmds[k] as AssignCmd).Lhss[0].DeepAssignedVariable;
            HavocCmd havoc = new HavocCmd(Token.NoToken,
              new List<IdentifierExpr> { new IdentifierExpr(v.tok, v) });
            b.Cmds[k] = havoc;
          }

          if (!(b.Cmds[k] is AssignCmd)) continue;
          foreach (var rhs in (b.Cmds[k] as AssignCmd).Rhss.OfType<IdentifierExpr>())
          {
            if (!(rhs.Name.StartsWith("$M.")))
              continue;

            Variable v = (b.Cmds[k] as AssignCmd).Lhss[0].DeepAssignedVariable;
            HavocCmd havoc = new HavocCmd(Token.NoToken,
              new List<IdentifierExpr> { new IdentifierExpr(v.tok, v) });
            b.Cmds[k] = havoc;
          }
        }
      }
    }

    private void AbstractWriteAccesses(InstrumentationRegion region)
    {
      foreach (var b in region.Blocks())
      {
        List<Cmd> cmdsToRemove = new List<Cmd>();

        for (int k = 0; k < b.Cmds.Count; k++)
        {
          if (!(b.Cmds[k] is AssignCmd)) continue;

          foreach (var lhs in (b.Cmds[k] as AssignCmd).Lhss.OfType<MapAssignLhs>())
          {
            if (!(lhs.DeepAssignedIdentifier.Name.StartsWith("$M.")) ||
                !(lhs.Map is SimpleAssignLhs) || lhs.Indexes.Count != 1)
              continue;

            cmdsToRemove.Add(b.Cmds[k]);
          }

          foreach (var lhs in (b.Cmds[k] as AssignCmd).Lhss.OfType<SimpleAssignLhs>())
          {
            if (!(lhs.DeepAssignedIdentifier.Name.StartsWith("$M.")))
              continue;

            cmdsToRemove.Add(b.Cmds[k]);
          }
        }

        foreach (var c in cmdsToRemove) b.Cmds.Remove(c);
      }
    }

    private void CleanUpModset(InstrumentationRegion region)
    {
      region.Procedure().Modifies.RemoveAll(val => !(val.Name.Equals("$Alloc") ||
        val.Name.Equals("$CurrAddr") || val.Name.Equals("CLS") ||
        val.Name.Contains("LS_$") ||
        val.Name.Contains("WRITTEN_$") || val.Name.Contains("READ_$")));
    }
  }
}
