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
using System.Diagnostics.Contracts;
using Microsoft.Boogie;
using Whoop.Domain.Drivers;

namespace Whoop
{
  public class Lock
  {
    private IdentifierExpr Ptr;
    private int Ixs;

    public readonly Constant Id;
    public readonly string Name;

    public readonly bool IsKernelSpecific;

    public Lock(Constant id)
    {
      this.Id = id;
      this.Name = id.Name;
      this.IsKernelSpecific = true;
    }

    public Lock(Constant id, Expr lockExpr)
    {
      this.Id = id;
      this.Name = id.Name;
      this.IsKernelSpecific = false;

      if (lockExpr is NAryExpr)
      {
        this.Ptr = (lockExpr as NAryExpr).Args[0] as IdentifierExpr;
        this.Ixs = ((lockExpr as NAryExpr).Args[1] as LiteralExpr).asBigNum.ToInt;
      }
      else if (lockExpr is IdentifierExpr)
      {
        this.Ptr = lockExpr as IdentifierExpr;
      }
    }

    public bool IsEqual(AnalysisContext ac, Implementation impl, Expr lockExpr)
    {
      if (this.Ptr == null)
        return false;
      if (lockExpr == null)
        return false;
      if (this.IsKernelSpecific)
        return false;

      IdentifierExpr ptr = null;
      if (lockExpr is NAryExpr)
      {
        ptr = (lockExpr as NAryExpr).Args[0] as IdentifierExpr;
        int ixs = ((lockExpr as NAryExpr).Args[1] as LiteralExpr).asBigNum.ToInt;
        if (this.Ixs != ixs)
          return false;
      }
      else
      {
        ptr = lockExpr as IdentifierExpr;
        if (lockExpr is IdentifierExpr &&
          ac.GetConstant((lockExpr as IdentifierExpr).Name) != null &&
          this.Ptr.Name.Equals((lockExpr as IdentifierExpr).Name))
        {
          return true;
        }
      }

      if (ptr == null)
        return false;

      int index = -1;
      for (int i = 0; i < impl.InParams.Count; i++)
      {
        if (impl.InParams[i].Name.Equals(ptr.Name))
          index = i;
      }

      if (index == -1)
        return false;

      Implementation initFunc = ac.GetImplementation(DeviceDriver.InitEntryPoint);

      foreach (var b in initFunc.Blocks)
      {
        foreach (var c in b.Cmds)
        {
          if (!(c is CallCmd))
            continue;
          if (!(c as CallCmd).callee.Equals(impl.Name))
            continue;

          IdentifierExpr id = (c as CallCmd).Ins[index] as IdentifierExpr;
          if (id.Name.Equals(this.Ptr.Name))
            return true;
        }
      }

      return false;
    }
  }
}
