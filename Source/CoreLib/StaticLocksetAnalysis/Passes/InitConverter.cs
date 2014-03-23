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

namespace whoop
{
  public class InitConverter
  {
    WhoopProgram wp;

    public InitConverter(WhoopProgram wp)
    {
      Contract.Requires(wp != null);
      this.wp = wp;
    }

    public void Run()
    {
      foreach (var impl in wp.GetImplementationsToAnalyse()) {
        CreateInitFunction(impl);
      }
    }

    private void CreateInitFunction(Implementation impl)
    {
      Contract.Requires(impl != null);
      string name = "init_" + impl.Name.Substring(5);

      List<Variable> inParams = new List<Variable>();
      foreach (var v in wp.initFunc.Proc.InParams) {
        inParams.Add(new Duplicator().VisitVariable(v.Clone() as Variable));
      }

      List<Variable> outParams = new List<Variable>();
      foreach (var v in wp.initFunc.Proc.OutParams) {
        outParams.Add(new Duplicator().VisitVariable(v.Clone() as Variable));
      }

      Procedure newProc = new Procedure(Token.NoToken, name,
                            new List<TypeVariable>(), inParams, outParams,
                            new List<Requires>(), new List<IdentifierExpr>(), new List<Ensures>());

      newProc.Attributes = new QKeyValue(Token.NoToken, "init", new List<object>(), null);

      List<Variable> localVars = new List<Variable>();
      foreach (var v in wp.initFunc.LocVars) {
        localVars.Add(new Duplicator().VisitVariable(v.Clone() as Variable));
      }

      List<Block> blocks = new List<Block>();
      foreach (var v in wp.initFunc.Blocks) {
        blocks.Add(new Duplicator().VisitBlock(v.Clone() as Block));
      }

      Implementation newImpl = new Implementation(Token.NoToken, name,
                                 new List<TypeVariable>(), inParams, outParams,
                                 localVars, blocks);

      newImpl.Proc = newProc;
      newImpl.Attributes = new QKeyValue(Token.NoToken, "init", new List<object>(), null);

      foreach (var v in wp.program.TopLevelDeclarations.OfType<GlobalVariable>()) {
        if (v.Name.Equals("$Alloc") || v.Name.Equals("$CurrAddr") || v.Name.Contains("$M.")) {
          newProc.Modifies.Add(new IdentifierExpr(Token.NoToken, v));
        }
      }

      wp.program.TopLevelDeclarations.Add(newProc);
      wp.program.TopLevelDeclarations.Add(newImpl);
      wp.resContext.AddProcedure(newProc);
    }
  }
}