using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Novalist.Core.Models;
using Novalist.Core.Services;

namespace Novalist.Desktop.ViewModels;

public partial class RelationshipsGraphViewModel : ObservableObject
{
    private readonly IEntityService _entityService;

    [ObservableProperty]
    private ObservableCollection<RelationshipNode> _nodes = [];

    [ObservableProperty]
    private ObservableCollection<RelationshipEdge> _edges = [];

    [ObservableProperty]
    private ObservableCollection<RelationshipGroupBox> _groupBoxes = [];

    [ObservableProperty]
    private bool _isLoading;

    public RelationshipsGraphViewModel(IEntityService entityService)
    {
        _entityService = entityService;
    }

    public async Task ReloadAsync()
    {
        IsLoading = true;
        try
        {
            var characters = await _entityService.LoadCharactersAsync();
            BuildGraph(characters.ToList());
        }
        finally
        {
            IsLoading = false;
        }
    }

    private static readonly string[] ParentRoles =
        ["vater", "mutter", "eltern", "father", "mother", "parent", "papa", "mama"];
    private static readonly string[] ChildRoles =
        ["kind", "tochter", "sohn", "child", "daughter", "son"];
    private static readonly string[] PartnerRoles =
        ["ehemann", "ehefrau", "ehepartner", "ehegatte", "gatte", "gattin",
         "partner", "partnerin", "lebensgefährte", "lebensgefährtin",
         "spouse", "husband", "wife", "verlobt", "fiancé", "fiancée",
         "mann", "frau", "ex-mann", "ex-frau"];
    private static readonly string[] SiblingRoles =
        ["bruder", "schwester", "geschwister", "brother", "sister", "sibling", "zwilling", "twin"];

    private static bool IsParentRole(string role) =>
        ParentRoles.Any(p => role.Contains(p, StringComparison.OrdinalIgnoreCase));
    private static bool IsChildRole(string role) =>
        ChildRoles.Any(c => role.Contains(c, StringComparison.OrdinalIgnoreCase));
    private static bool IsPartnerRole(string role) =>
        PartnerRoles.Any(p => role.Contains(p, StringComparison.OrdinalIgnoreCase));
    private static bool IsSiblingRoleS(string role) =>
        SiblingRoles.Any(s => role.Contains(s, StringComparison.OrdinalIgnoreCase));
    private static bool IsFamilyRole(string role) =>
        IsParentRole(role) || IsChildRole(role) || IsPartnerRole(role) || IsSiblingRoleS(role);

    private void BuildGraph(IReadOnlyList<CharacterData> characters)
    {
        Nodes.Clear();
        Edges.Clear();
        GroupBoxes.Clear();
        if (characters.Count == 0) return;

        // Resolve target name → CharacterData. Drop characters with no relationships
        // (incoming or outgoing) so the graph only shows the connected subset.
        var byName = new Dictionary<string, CharacterData>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in characters)
        {
            if (!string.IsNullOrWhiteSpace(c.DisplayName)) byName[c.DisplayName] = c;
            if (!string.IsNullOrWhiteSpace(c.Name)) byName.TryAdd(c.Name, c);
        }

        var connectedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in characters)
        {
            foreach (var rel in c.Relationships)
            {
                foreach (var target in rel.Target.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
                {
                    if (!byName.TryGetValue(target, out var t)) continue;
                    connectedIds.Add(c.Id);
                    connectedIds.Add(t.Id);
                }
            }
        }

        var connected = characters.Where(c => connectedIds.Contains(c.Id)).ToList();
        if (connected.Count == 0) return;

        // Build family adjacency: parent→child + partner pairs.
        var parentOf = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var childrenOf = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var partnerOf = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        void AddFamilyLink(string parentName, string childName)
        {
            if (string.IsNullOrWhiteSpace(parentName) || string.IsNullOrWhiteSpace(childName)) return;
            if (!parentOf.TryGetValue(childName, out var parents)) { parents = new(StringComparer.OrdinalIgnoreCase); parentOf[childName] = parents; }
            parents.Add(parentName);
            if (!childrenOf.TryGetValue(parentName, out var kids)) { kids = new(StringComparer.OrdinalIgnoreCase); childrenOf[parentName] = kids; }
            kids.Add(childName);
        }
        void AddPartner(string a, string b)
        {
            if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b)) return;
            if (!partnerOf.TryGetValue(a, out var pa)) { pa = new(StringComparer.OrdinalIgnoreCase); partnerOf[a] = pa; }
            pa.Add(b);
            if (!partnerOf.TryGetValue(b, out var pb)) { pb = new(StringComparer.OrdinalIgnoreCase); partnerOf[b] = pb; }
            pb.Add(a);
        }

        foreach (var c in connected)
        {
            foreach (var rel in c.Relationships)
            {
                if (!IsFamilyRole(rel.Role)) continue;
                foreach (var target in rel.Target.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
                {
                    if (!byName.TryGetValue(target, out var t)) continue;
                    if (IsParentRole(rel.Role)) AddFamilyLink(t.DisplayName, c.DisplayName);
                    else if (IsChildRole(rel.Role)) AddFamilyLink(c.DisplayName, t.DisplayName);
                    else if (IsPartnerRole(rel.Role)) AddPartner(c.DisplayName, t.DisplayName);
                }
            }
        }

        // Co-parents are implicit partners (share at least one child).
        foreach (var kv in childrenOf)
        {
            // unused - co-parent inference handled below
        }
        foreach (var c in connected)
        {
            var name = c.DisplayName;
            if (!childrenOf.TryGetValue(name, out var myKids)) continue;
            foreach (var kid in myKids)
            {
                if (!parentOf.TryGetValue(kid, out var kidParents)) continue;
                foreach (var coParent in kidParents)
                {
                    if (!string.Equals(coParent, name, StringComparison.OrdinalIgnoreCase))
                        AddPartner(name, coParent);
                }
            }
        }

        // Family clusters: BFS via parent/child/partner edges.
        var nameToId = connected.ToDictionary(c => c.DisplayName, c => c.Id, StringComparer.OrdinalIgnoreCase);
        var familyOf = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        int familyIdx = 0;
        foreach (var c in connected)
        {
            if (familyOf.ContainsKey(c.DisplayName)) continue;
            if (!parentOf.ContainsKey(c.DisplayName) && !childrenOf.ContainsKey(c.DisplayName) && !partnerOf.ContainsKey(c.DisplayName)) continue;
            var stack = new Stack<string>();
            stack.Push(c.DisplayName);
            while (stack.Count > 0)
            {
                var n = stack.Pop();
                if (!familyOf.TryAdd(n, familyIdx)) continue;
                if (parentOf.TryGetValue(n, out var ps)) foreach (var p in ps) stack.Push(p);
                if (childrenOf.TryGetValue(n, out var ks)) foreach (var k in ks) stack.Push(k);
                if (partnerOf.TryGetValue(n, out var prs)) foreach (var p in prs) stack.Push(p);
            }
            familyIdx++;
        }

        // Layout per family: row-per-generation, children centered under
        // their parents' midpoint. T-edges connect couples to their kids.
        const double familyTopMargin = 80;
        const double horizSpacing = 110;
        const double vertSpacing = 130;
        const double partnerSpacing = 130; // gap between paired partners
        const double leftMargin = 60;
        const double familyGap = 100;
        var positions = new Dictionary<string, (double x, double y)>(StringComparer.OrdinalIgnoreCase);
        var coupleChildren = new List<(List<string> Parents, List<string> Children)>();
        double familyLeftX = leftMargin;

        // ── Pre-place loose members of multi-endpoint role groups ─────
        // Loose = not in any family. They get a horizontal row at top-left
        // BEFORE families, so role bounding boxes (Ring, etc.) stay compact
        // on the left and don't envelop family boxes downstream.
        var prePlacedLoose = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var roleEndpointsByRole = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in connected)
        {
            foreach (var rel in c.Relationships)
            {
                if (IsFamilyRole(rel.Role)) continue;
                foreach (var target in rel.Target.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
                {
                    if (!nameToId.ContainsKey(target)) continue;
                    if (!roleEndpointsByRole.TryGetValue(rel.Role, out var set))
                    {
                        set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        roleEndpointsByRole[rel.Role] = set;
                    }
                    set.Add(c.DisplayName);
                    set.Add(target);
                }
            }
        }

        double looseCursorX = leftMargin;
        const double looseSpacing = 110;
        foreach (var (role, eps) in roleEndpointsByRole)
        {
            if (eps.Count < 3) continue;
            var looseMembers = eps.Where(n => !familyOf.ContainsKey(n)).ToList();
            if (looseMembers.Count == 0) continue;
            foreach (var m in looseMembers)
            {
                if (prePlacedLoose.Contains(m)) continue;
                positions[m] = (looseCursorX + 40, familyTopMargin);
                prePlacedLoose.Add(m);
                looseCursorX += looseSpacing;
            }
        }
        if (prePlacedLoose.Count > 0)
            familyLeftX = looseCursorX + familyGap;

        for (int fi = 0; fi < familyIdx; fi++)
        {
            var members = familyOf.Where(kv => kv.Value == fi).Select(kv => kv.Key).ToList();
            if (members.Count == 0) continue;
            var memberSet = new HashSet<string>(members, StringComparer.OrdinalIgnoreCase);

            // Generation = max chain of parents inside cluster.
            var generation = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            int ComputeGen(string n, HashSet<string> seen)
            {
                if (generation.TryGetValue(n, out var g)) return g;
                if (!seen.Add(n)) { generation[n] = 0; return 0; }
                int max = 0;
                if (parentOf.TryGetValue(n, out var ps))
                    foreach (var p in ps)
                        if (memberSet.Contains(p))
                            max = Math.Max(max, ComputeGen(p, seen) + 1);
                generation[n] = max;
                return max;
            }
            foreach (var m in members) ComputeGen(m, new HashSet<string>(StringComparer.OrdinalIgnoreCase));

            // Partner-share: same generation; pull partners to the same gen.
            bool changed;
            do
            {
                changed = false;
                foreach (var m in members)
                {
                    if (!partnerOf.TryGetValue(m, out var prs)) continue;
                    foreach (var p in prs)
                    {
                        if (!memberSet.Contains(p)) continue;
                        if (generation[p] < generation[m]) { generation[p] = generation[m]; changed = true; }
                    }
                }
            } while (changed);

            int maxGen = members.Max(m => generation[m]);

            // Build couple groups: same set of parents for kids in cluster.
            // Key = sorted list of parent names (case-insensitive).
            var coupleToKids = new Dictionary<string, (List<string> Parents, List<string> Children)>(StringComparer.OrdinalIgnoreCase);
            foreach (var m in members)
            {
                if (!parentOf.TryGetValue(m, out var ps)) continue;
                var inCluster = ps.Where(memberSet.Contains).OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToList();
                if (inCluster.Count == 0) continue;
                var key = string.Join("|", inCluster);
                if (!coupleToKids.TryGetValue(key, out var entry))
                {
                    entry = (inCluster, new List<string>());
                    coupleToKids[key] = entry;
                }
                entry.Children.Add(m);
            }

            // Lay out top-down. At gen 0 place partner-pairs adjacent.
            double cursorX = familyLeftX;
            var placedAtGen = new Dictionary<int, List<string>>();
            for (int g = 0; g <= maxGen; g++) placedAtGen[g] = new List<string>();

            // Group gen 0 into partner units, then singletons.
            var gen0 = members.Where(m => generation[m] == 0).ToList();
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var gen0Units = new List<List<string>>();
            foreach (var m in gen0)
            {
                if (visited.Contains(m)) continue;
                var unit = new List<string> { m };
                visited.Add(m);
                if (partnerOf.TryGetValue(m, out var prs))
                {
                    foreach (var p in prs.OrderBy(s => s, StringComparer.OrdinalIgnoreCase))
                    {
                        if (!visited.Contains(p) && memberSet.Contains(p) && generation[p] == 0)
                        {
                            unit.Add(p);
                            visited.Add(p);
                        }
                    }
                }
                gen0Units.Add(unit);
            }

            // Place gen 0
            double y0 = familyTopMargin;
            foreach (var unit in gen0Units)
            {
                for (int i = 0; i < unit.Count; i++)
                {
                    positions[unit[i]] = (cursorX + i * partnerSpacing, y0);
                    placedAtGen[0].Add(unit[i]);
                }
                cursorX += unit.Count * partnerSpacing;
                cursorX += horizSpacing * 0.4; // small gap between units
            }
            cursorX -= horizSpacing * 0.4; // remove trailing pad

            // Place subsequent generations: each child centered under its parents' midpoint.
            for (int g = 1; g <= maxGen; g++)
            {
                var rowMembers = members.Where(m => generation[m] == g).ToList();
                // Compute desired X = avg parent X.
                var desired = new List<(string Name, double X)>();
                foreach (var m in rowMembers)
                {
                    double xSum = 0; int cnt = 0;
                    if (parentOf.TryGetValue(m, out var ps))
                    {
                        foreach (var p in ps)
                            if (positions.TryGetValue(p, out var pp)) { xSum += pp.x; cnt++; }
                    }
                    var dx = cnt > 0 ? xSum / cnt : cursorX;
                    desired.Add((m, dx));
                }
                desired.Sort((a, b) => a.X.CompareTo(b.X));

                // Resolve overlaps left→right.
                double prevX = double.NegativeInfinity;
                var rowY = familyTopMargin + g * vertSpacing;
                foreach (var (name, x) in desired)
                {
                    var px = Math.Max(x, prevX + horizSpacing);
                    positions[name] = (px, rowY);
                    placedAtGen[g].Add(name);
                    prevX = px;
                }

                // Re-center each child group under their parents' midpoint when
                // possible (shift child X back toward midpoint if no collision).
                foreach (var entry in coupleToKids.Values)
                {
                    var kidsInRow = entry.Children.Where(k => generation[k] == g).ToList();
                    if (kidsInRow.Count == 0) continue;
                    if (!entry.Parents.All(p => positions.ContainsKey(p))) continue;
                    var parentMidX = entry.Parents.Average(p => positions[p].x);
                    var kidsAvg = kidsInRow.Average(k => positions[k].x);
                    var delta = parentMidX - kidsAvg;
                    if (Math.Abs(delta) < 1) continue;
                    // Shift kids together; clamp so they don't collide with neighbors.
                    var sortedRow = placedAtGen[g].Select(n => (Name: n, X: positions[n].x)).OrderBy(x => x.X).ToList();
                    var kidSet = new HashSet<string>(kidsInRow, StringComparer.OrdinalIgnoreCase);
                    var blockMinX = kidsInRow.Min(k => positions[k].x) + delta;
                    var blockMaxX = kidsInRow.Max(k => positions[k].x) + delta;
                    bool canShift = true;
                    foreach (var (name, x) in sortedRow)
                    {
                        if (kidSet.Contains(name)) continue;
                        if (x >= blockMinX - horizSpacing && x <= blockMaxX + horizSpacing) { canShift = false; break; }
                    }
                    if (canShift)
                    {
                        foreach (var k in kidsInRow)
                        {
                            var p = positions[k];
                            positions[k] = (p.x + delta, p.y);
                        }
                    }
                }
            }

            // Record couple→kids for T-edges (one entry per couple set).
            foreach (var entry in coupleToKids.Values)
                coupleChildren.Add((entry.Parents, entry.Children));

            // Advance familyLeftX past the rightmost member + half nodeW + family box padding.
            var familyMaxX = members.Max(m => positions[m].x);
            familyLeftX = familyMaxX + 40 /*half node*/ + 24 /*box padding*/ + familyGap;
        }

        // Pseudo-family anchoring: external character connected to a family
        // member via sibling/cousin/uncle/etc. → place adjacent to that family
        // member (chosen by oldest generation when multiple targets).
        // Build per-node candidate anchors first.
        string[] pseudoRoles = [
            "bruder","schwester","geschwister","brother","sister","sibling","zwilling","twin",
            "cousin","cousine","vetter","base",
            "onkel","tante","uncle","aunt",
            "neffe","nichte","nephew","niece",
            "großvater","grossvater","großmutter","grossmutter","oma","opa","grandfather","grandmother","grandparent",
            "enkel","enkelin","grandchild","grandson","granddaughter",
            "stiefbruder","stiefschwester","halbbruder","halbschwester","stepbrother","stepsister","half-brother","half-sister",
            "schwager","schwägerin","brother-in-law","sister-in-law",
        ];
        bool IsPseudoRole(string r) => pseudoRoles.Any(p => r.Contains(p, StringComparison.OrdinalIgnoreCase));

        // Generation lookup per family member (already computed per family — recompute global map).
        var globalGen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, _) in familyOf)
        {
            int g = 0;
            var stack = new Stack<string>();
            stack.Push(name);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            while (stack.Count > 0)
            {
                var n = stack.Pop();
                if (!seen.Add(n)) continue;
                if (parentOf.TryGetValue(n, out var ps))
                    foreach (var p in ps) if (familyOf.ContainsKey(p)) { g++; stack.Push(p); break; }
            }
            globalGen[name] = g;
        }

        // Collect ALL pseudo-family targets per external (not just oldest).
        var anchorTargets = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in connected)
        {
            if (familyOf.ContainsKey(c.DisplayName) || prePlacedLoose.Contains(c.DisplayName)) continue;
            var targets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var rel in c.Relationships)
            {
                if (!IsPseudoRole(rel.Role)) continue;
                foreach (var target in rel.Target.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
                {
                    if (!byName.TryGetValue(target, out var td)) continue;
                    if (!familyOf.ContainsKey(td.DisplayName)) continue;
                    targets.Add(td.DisplayName);
                }
            }
            foreach (var other in connected)
            {
                if (!familyOf.ContainsKey(other.DisplayName)) continue;
                foreach (var rel in other.Relationships)
                {
                    if (!IsPseudoRole(rel.Role)) continue;
                    foreach (var t in rel.Target.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
                    {
                        if (!string.Equals(t, c.DisplayName, StringComparison.OrdinalIgnoreCase)) continue;
                        targets.Add(other.DisplayName);
                    }
                }
            }
            if (targets.Count > 0)
                anchorTargets[c.DisplayName] = targets.OrderBy(t => globalGen.TryGetValue(t, out var g) ? g : 0).ToList();
        }

        // Build family bounds.
        var familyBounds = new Dictionary<int, (double minX, double minY, double maxX, double maxY)>();
        for (int fi = 0; fi < familyIdx; fi++)
        {
            var members = familyOf.Where(kv => kv.Value == fi).Select(kv => kv.Key).Where(positions.ContainsKey).ToList();
            if (members.Count == 0) continue;
            var fminX = members.Min(m => positions[m].x);
            var fminY = members.Min(m => positions[m].y);
            var fmaxX = members.Max(m => positions[m].x);
            var fmaxY = members.Max(m => positions[m].y);
            familyBounds[fi] = (fminX, fminY, fmaxX, fmaxY);
        }

        // Place anchored externals.
        var anchorStackCount = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var (ext, targets) in anchorTargets)
        {
            if (targets.Count == 0) continue;

            // Targets' generations.
            var targetGens = targets.Select(t => globalGen.TryGetValue(t, out var g) ? g : 0).Distinct().ToList();
            var familyIdxFor = familyOf[targets[0]];

            if (targetGens.Count == 1)
            {
                // Single generation: stack above the (oldest) target as before.
                var anchor = targets[0];
                if (!positions.TryGetValue(anchor, out var apos)) continue;
                anchorStackCount.TryGetValue(anchor, out var idx);
                anchorStackCount[anchor] = idx + 1;
                positions[ext] = (apos.x, apos.y - vertSpacing * (idx + 1));
            }
            else
            {
                // Multi-generation targets: place RIGHT of own family at midY
                // of the targets. If that collides with another family box,
                // shift that family (and every family further right) over.
                if (!familyBounds.TryGetValue(familyIdxFor, out var fb)) continue;
                var midY = targets.Where(t => positions.ContainsKey(t)).Average(t => positions[t].y);
                double extX = fb.maxX + horizSpacing;
                positions[ext] = (extX, midY);

                const double extPad = 24;
                double extLeft = extX - 40;
                double extRight = extX + 40;
                double extTop = midY - 14;
                double extBot = midY + 14;

                // Find any other family whose bbox overlaps the ext rect.
                int? blockingIdx = null;
                double blockingMinX = double.MaxValue;
                foreach (var (idx, ob) in familyBounds)
                {
                    if (idx == familyIdxFor) continue;
                    bool xOverlap = extRight + extPad >= ob.minX && extLeft - extPad <= ob.maxX;
                    bool yOverlap = extBot + extPad >= ob.minY && extTop - extPad <= ob.maxY;
                    if (xOverlap && yOverlap && ob.minX < blockingMinX)
                    {
                        blockingMinX = ob.minX;
                        blockingIdx = idx;
                    }
                }
                if (blockingIdx.HasValue)
                {
                    var delta = extRight + extPad + familyGap - blockingMinX;
                    if (delta > 0)
                    {
                        // Shift the blocking family + all families to its right.
                        foreach (var (idx, ob) in familyBounds.ToList())
                        {
                            if (idx == familyIdxFor) continue;
                            if (ob.minX < blockingMinX) continue;
                            // Shift all member positions for this family.
                            var membersToShift = familyOf.Where(kv => kv.Value == idx).Select(kv => kv.Key).ToList();
                            foreach (var m in membersToShift)
                            {
                                if (!positions.TryGetValue(m, out var p)) continue;
                                positions[m] = (p.x + delta, p.y);
                            }
                            familyBounds[idx] = (ob.minX + delta, ob.minY, ob.maxX + delta, ob.maxY);
                        }
                        familyLeftX = Math.Max(familyLeftX, blockingMinX + delta + 100);
                    }
                }
            }
        }

        // Unfamily connected nodes — circular layout below families.
        var loose = connected
            .Where(c => !familyOf.ContainsKey(c.DisplayName)
                     && !prePlacedLoose.Contains(c.DisplayName)
                     && !anchorTargets.ContainsKey(c.DisplayName)
                     && !positions.ContainsKey(c.DisplayName))
            .ToList();
        if (loose.Count > 0)
        {
            // Remaining loose nodes (not part of any role group) placed below
            // family rows in a circular cluster.
            var maxFamilyY = positions.Count > 0 ? positions.Values.Max(p => p.y) : familyTopMargin;
            double centerX = 480, centerY = maxFamilyY + 250;
            double radius = Math.Min(320, 100 + loose.Count * 14);
            for (int i = 0; i < loose.Count; i++)
            {
                var angle = i * 2 * Math.PI / loose.Count - Math.PI / 2;
                positions[loose[i].DisplayName] = (centerX + radius * Math.Cos(angle), centerY + radius * Math.Sin(angle));
            }
        }

        // Materialize nodes.
        var nodeByName = new Dictionary<string, RelationshipNode>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in connected)
        {
            if (!positions.TryGetValue(c.DisplayName, out var pos)) continue;
            var node = new RelationshipNode { Id = c.Id, Name = c.DisplayName, X = pos.x - 40, Y = pos.y - 14 };
            Nodes.Add(node);
            nodeByName[c.DisplayName] = node;
            if (!string.IsNullOrWhiteSpace(c.Name)) nodeByName.TryAdd(c.Name, node);
        }

        // Per-family bounding box around all members. Suppress all parent/child
        // edges (spatial layout already implies hierarchy) so only non-family
        // edges (Freund, Ring, etc.) remain as lines.
        const double boxPadding = 20;
        const double nodeW = 80;
        const double nodeH = 28;

        for (int fi = 0; fi < familyIdx; fi++)
        {
            var familyNames = familyOf.Where(kv => kv.Value == fi).Select(kv => kv.Key).ToList();
            double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
            string? sampleSurname = null;
            foreach (var name in familyNames)
            {
                if (!nodeByName.TryGetValue(name, out var n)) continue;
                minX = Math.Min(minX, n.X);
                minY = Math.Min(minY, n.Y);
                maxX = Math.Max(maxX, n.X + nodeW);
                maxY = Math.Max(maxY, n.Y + nodeH);
                if (sampleSurname == null && characters.FirstOrDefault(c => string.Equals(c.DisplayName, name, StringComparison.OrdinalIgnoreCase)) is { } cd
                    && !string.IsNullOrWhiteSpace(cd.Surname))
                    sampleSurname = cd.Surname;
            }
            if (minX == double.MaxValue) continue;
            GroupBoxes.Add(new RelationshipGroupBox
            {
                X = minX - boxPadding,
                Y = minY - boxPadding,
                Width = (maxX - minX) + 2 * boxPadding,
                Height = (maxY - minY) + 2 * boxPadding,
                Label = string.IsNullOrWhiteSpace(sampleSurname) ? "Familie" : $"Familie {sampleSurname}",
            });
        }

        static string NormKey(string s) => s.Trim().ToLowerInvariant();

        // T-edges from each couple set down to each child.
        const double nW = 80;
        const double nH = 28;
        foreach (var (parents, kids) in coupleChildren)
        {
            var presentParents = parents.Where(p => positions.ContainsKey(p)).ToList();
            var presentKids = kids.Where(k => positions.ContainsKey(k)).ToList();
            if (presentParents.Count == 0 || presentKids.Count == 0) continue;

            // positions store CENTER y; offset by nH/2 to reach top/bottom of nodes.
            var parentBottomY = presentParents.Max(p => positions[p].y) + nH / 2;
            var childTopY = presentKids.Min(k => positions[k].y) - nH / 2;
            var midY = (parentBottomY + childTopY) / 2;
            var parentMidX = presentParents.Average(p => positions[p].x); // node center
            var kidsMinX = presentKids.Min(k => positions[k].x);
            var kidsMaxX = presentKids.Max(k => positions[k].x);

            // Vertical from parent-midpoint down to bar.
            Edges.Add(new RelationshipEdge { X1 = parentMidX, Y1 = parentBottomY, X2 = parentMidX, Y2 = midY });
            // Horizontal bar spanning min(parentMid, kidsMinX) → max(parentMid, kidsMaxX).
            var barLeftX = Math.Min(parentMidX, kidsMinX);
            var barRightX = Math.Max(parentMidX, kidsMaxX);
            Edges.Add(new RelationshipEdge { X1 = barLeftX, Y1 = midY, X2 = barRightX, Y2 = midY });
            // Verticals from bar down to each child top.
            foreach (var k in presentKids)
            {
                var kx = positions[k].x;
                Edges.Add(new RelationshipEdge { X1 = kx, Y1 = midY, X2 = kx, Y2 = childTopY });
            }
        }

        // Suppress parent/child + sibling + partner edges (replaced by T-line +
        // family box) so they don't render as labeled segments.
        var familyEdgePairs = new HashSet<(string, string)>();
        foreach (var c in connected)
        {
            foreach (var rel in c.Relationships)
            {
                if (!IsFamilyRole(rel.Role)) continue;
                foreach (var target in rel.Target.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
                {
                    if (!nameToId.ContainsKey(target)) continue;
                    if (familyOf.TryGetValue(c.DisplayName, out var fA)
                        && familyOf.TryGetValue(target, out var fB)
                        && fA == fB)
                    {
                        familyEdgePairs.Add((NormKey(c.DisplayName), NormKey(target)));
                        familyEdgePairs.Add((NormKey(target), NormKey(c.DisplayName)));
                    }
                }
            }
        }

        // Collect remaining (non-family) relationships, grouped by role.
        var nonFamilyByRole = new Dictionary<string, List<(string From, string To)>>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in connected)
        {
            if (!nodeByName.ContainsKey(c.DisplayName)) continue;
            foreach (var rel in c.Relationships)
            {
                foreach (var target in rel.Target.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
                {
                    if (!nodeByName.ContainsKey(target)) continue;
                    var pair = (NormKey(c.DisplayName), NormKey(target));
                    if (familyEdgePairs.Contains(pair)) continue;
                    if (!nonFamilyByRole.TryGetValue(rel.Role, out var list))
                    {
                        list = new();
                        nonFamilyByRole[rel.Role] = list;
                    }
                    list.Add((c.DisplayName, target));
                }
            }
        }

        // For each role: if endpoints form a group of ≥3 nodes, draw one box
        // labeled with the role and skip the individual lines. Otherwise emit
        // pairwise edges as before.
        var clusteredPairs = new HashSet<(string, string)>();
        foreach (var (role, pairs) in nonFamilyByRole)
        {
            var endpoints = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (f, t) in pairs) { endpoints.Add(f); endpoints.Add(t); }
            if (endpoints.Count < 3) continue;

            // Bounding box only over the LOOSE endpoints (not in any family),
            // keeping the role group compact in its top-left strip. Family-side
            // endpoints stay in their family and still drop their labeled edge
            // (suppressed below) but the box doesn't try to wrap them.
            double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
            foreach (var n in endpoints)
            {
                if (familyOf.ContainsKey(n)) continue;
                if (!nodeByName.TryGetValue(n, out var nd)) continue;
                minX = Math.Min(minX, nd.X);
                minY = Math.Min(minY, nd.Y);
                maxX = Math.Max(maxX, nd.X + nW);
                maxY = Math.Max(maxY, nd.Y + nH);
            }
            if (minX == double.MaxValue) continue;

            const double pad = 14;
            // Greedy expand box to include each family-side endpoint, ordered
            // by X, as long as the expansion doesn't envelop a non-member node.
            var familySide = endpoints
                .Where(n => familyOf.ContainsKey(n) && nodeByName.ContainsKey(n))
                .OrderBy(n => nodeByName[n].X)
                .ToList();

            var endpointSet = new HashSet<string>(endpoints, StringComparer.OrdinalIgnoreCase);
            foreach (var name in familySide)
            {
                var nd = nodeByName[name];
                var newMinX = Math.Min(minX, nd.X);
                var newMinY = Math.Min(minY, nd.Y);
                var newMaxX = Math.Max(maxX, nd.X + nW);
                var newMaxY = Math.Max(maxY, nd.Y + nH);

                // Check no OTHER node would be enveloped by the expanded box.
                bool collides = false;
                foreach (var other in nodeByName.Values)
                {
                    if (endpointSet.Contains(other.Name)) continue;
                    var cx = other.X + nW / 2;
                    var cy = other.Y + nH / 2;
                    if (cx >= newMinX - pad && cx <= newMaxX + pad
                        && cy >= newMinY - pad && cy <= newMaxY + pad)
                    {
                        collides = true;
                        break;
                    }
                }
                if (collides) continue;
                minX = newMinX; minY = newMinY; maxX = newMaxX; maxY = newMaxY;
            }

            GroupBoxes.Add(new RelationshipGroupBox
            {
                X = minX - pad,
                Y = minY - pad,
                Width = (maxX - minX) + 2 * pad,
                Height = (maxY - minY) + 2 * pad,
                Label = role,
            });

            foreach (var (f, t) in pairs)
            {
                clusteredPairs.Add((NormKey(f), NormKey(t)));
                clusteredPairs.Add((NormKey(t), NormKey(f)));
            }
        }

        // Merge edges by unordered pair so reciprocal roles (e.g. Schwester ↔ Tante)
        // render as a single labeled line "Schwester / Tante".
        var pairRoles = new Dictionary<(string A, string B), List<string>>();
        foreach (var (role, pairs) in nonFamilyByRole)
        {
            foreach (var (f, t) in pairs)
            {
                if (clusteredPairs.Contains((NormKey(f), NormKey(t)))) continue;
                if (!nodeByName.ContainsKey(f) || !nodeByName.ContainsKey(t)) continue;
                var a = NormKey(f);
                var b = NormKey(t);
                var key = string.Compare(a, b, StringComparison.Ordinal) <= 0 ? (a, b) : (b, a);
                if (!pairRoles.TryGetValue(key, out var roles))
                {
                    roles = new List<string>();
                    pairRoles[key] = roles;
                }
                if (!roles.Any(r => string.Equals(r, role, StringComparison.OrdinalIgnoreCase)))
                    roles.Add(role);
            }
        }

        // Resolve normalized pair → display nodes by first-seen mapping.
        var normToDisplay = new Dictionary<string, RelationshipNode>(StringComparer.OrdinalIgnoreCase);
        foreach (var n in nodeByName.Values) normToDisplay[NormKey(n.Name)] = n;

        foreach (var (key, roles) in pairRoles)
        {
            if (!normToDisplay.TryGetValue(key.A, out var fromNode)) continue;
            if (!normToDisplay.TryGetValue(key.B, out var toNode)) continue;
            Edges.Add(new RelationshipEdge
            {
                X1 = fromNode.X + 40,
                Y1 = fromNode.Y + 14,
                X2 = toNode.X + 40,
                Y2 = toNode.Y + 14,
                Label = string.Join(" / ", roles),
                LabelX = (fromNode.X + toNode.X) / 2 + 40,
                LabelY = (fromNode.Y + toNode.Y) / 2 + 14,
            });
        }
    }
}

public sealed class RelationshipNode
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public double X { get; init; }
    public double Y { get; init; }
}

public sealed class RelationshipEdge
{
    public double X1 { get; init; }
    public double Y1 { get; init; }
    public double X2 { get; init; }
    public double Y2 { get; init; }
    public string Label { get; init; } = string.Empty;
    public double LabelX { get; init; }
    public double LabelY { get; init; }

    public Avalonia.Point StartPoint => new(X1, Y1);
    public Avalonia.Point EndPoint => new(X2, Y2);
}

public sealed class RelationshipGroupBox
{
    public double X { get; init; }
    public double Y { get; init; }
    public double Width { get; set; }
    public double Height { get; init; }
    public string Label { get; init; } = string.Empty;
    /// <summary>Family-side endpoint names rendered as chips inside the box.</summary>
    public List<string> ExtraMembers { get; init; } = new();
}
