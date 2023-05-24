// SPDX-FileCopyrightText: 2023 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

namespace Nethermind.Verkle.Tree.Sync;

public class GetLeafNodesRequest
{
    public byte[] RootHash { get; set; }

    public byte[][] LeafNodePaths { get; set; }
}