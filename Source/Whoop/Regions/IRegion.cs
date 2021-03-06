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
using System.Linq;
using System.Text;
using Microsoft.Boogie;

namespace Whoop.Regions
{
  public interface IRegion
  {
    object Identifier();

    Block Header();

    Implementation Implementation();

    Procedure Procedure();

    IEnumerable<Cmd> Cmds();

    void AddInvariant(PredicateCmd pc);

    List<PredicateCmd> RemoveInvariants();
  }
}
