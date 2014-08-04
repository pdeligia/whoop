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

namespace Whoop.Summarisation
{
  internal class AccessCheckingSummaryGeneration : SummaryGeneration, IAccessCheckingSummaryGeneration
  {
    public AccessCheckingSummaryGeneration(AnalysisContext ac, EntryPoint ep)
      : base(ac, ep)
    {

    }

    public void Run()
    {
      if (WhoopCommandLineOptions.Get().MeasurePassExecutionTime)
      {
        base.Timer = new ExecutionTimer();
        base.Timer.Start();
      }

      foreach (var region in base.AC.InstrumentationRegions)
      {
        if (!base.EP.Name.Equals(region.Implementation().Name))
          continue;
        base.InstrumentEnsuresLocksetCandidates(region, base.AC.GetAccessCheckingVariables(), true);
        base.InstrumentEnsuresLocksetCandidates(region, base.AC.GetAccessCheckingVariables(), false);
      }

      foreach (var region in base.AC.InstrumentationRegions)
      {
        if (base.EP.Name.Equals(region.Implementation().Name))
          continue;
        base.InstrumentRequiresLocksetCandidates(region, base.AC.GetAccessCheckingVariables(), true);
        base.InstrumentRequiresLocksetCandidates(region, base.AC.GetAccessCheckingVariables(), false);
        base.InstrumentEnsuresLocksetCandidates(region, base.AC.GetAccessCheckingVariables(), true, true);
        base.InstrumentEnsuresLocksetCandidates(region, base.AC.GetAccessCheckingVariables(), false, true);
      }

      base.InstrumentExistentialBooleans();

      if (WhoopCommandLineOptions.Get().MeasurePassExecutionTime)
      {
        base.Timer.Stop();
        Console.WriteLine(" |  |------ [AccessCheckingSummaryGeneration] {0}", base.Timer.Result());
      }
    }

    #region helper functions

    protected override Constant CreateConstant()
    {
      Constant cons = new Constant(Token.NoToken, new TypedIdent(Token.NoToken, "_b$ac$" +
        base.EP.Name + "$" + base.Counter, Microsoft.Boogie.Type.Bool), false);
      base.ExistentialBooleans.Add(cons);
      base.Counter++;
      return cons;
    }

    #endregion
  }
}
