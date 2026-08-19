# Smart solver design

NeatWin Smart mode is a deterministic geometric optimizer. It does not call an AI service and it does not choose a tiling template.

Its input is the set of windows that are *actually exposed on screen* at the instant the user requests a tidy. The current arrangement is treated as a noisy observation of user intent.

## 1. State

For every participating window `i`, Smart keeps four edge variables:

```text
x_i = [left_i, top_i, right_i, bottom_i]
```

The baseline `x_i^0` is the current visual DWM frame, after mandatory off-screen recovery when that feature is enabled.

The active foreground window receives a larger preservation weight. A partly occluded window receives a slightly smaller one because its exact current rectangle is a weaker signal of immediate user intent.

## 2. Relationship inference

Smart creates a sparse constraint graph rather than comparing every possible layout template.

### Horizontal adjacency

Two windows become horizontal-neighbor candidates when:

- their vertical overlap ratio is high enough; and
- the distance between the right edge of the left window and the left edge of the right window is inside the neighbor inference radius.

The desired relation is:

```text
right_leftWindow = left_rightWindow
```

The same logic is applied vertically.

### Alignment

Near-matching outer edges become candidate equalities when the windows overlap enough on the orthogonal axis:

```text
top_A = top_B
bottom_A = bottom_B
left_A = left_B
right_A = right_B
```

### Screen anchors

A window edge near a monitor work-area edge becomes an anchor candidate:

```text
left_i = workArea.left
right_i = workArea.right
...
```

### Size preservation

For a resizable window, Smart can separately resist changing its width and height:

```text
right_i - left_i = original_width_i
bottom_i - top_i = original_height_i
```

This separates two ideas that a simple maximum-resize percentage cannot express: the **preference** to solve a relationship by translating a window instead of resizing it, and the **hard upper bound** on how much resizing is allowed at all.

### Confidence

Relationship confidence decays smoothly with distance using a Gaussian kernel:

```text
c(d) = exp(-0.5 * (d / sigma)^2)
```

This avoids a purely binary interpretation where one nearby distance is treated as related and a slightly larger one as completely unrelated.

Orthogonal overlap is multiplied into the confidence of neighbor relations, so two windows that barely touch vertically are much weaker horizontal-neighbor candidates than two windows sharing most of their height.

## 3. Objective

Conceptually Smart minimizes a quadratic energy of the form:

```text
E =
    W_preserve * Σ importance_i * ||x_i - x_i^0||²
  + W_resize   * Σ importance_i * size_error_i²
  + W_order    * Σ confidence_k * relation_error_k²
  + W_screen   * Σ confidence_s * screen_anchor_error_s²
```

where `size_error_i` represents the deviation from the original width and height for a resizable window.

The current implementation does not depend on a heavyweight general-purpose QP package. Because the inferred graph is sparse and the intended correction is local, it approximates the weighted least-squares solution with damped Jacobi relaxation.

Each edge receives proposals from:

- its original baseline position;
- the opposite edge of the same window when size preservation is enabled;
- related edges of neighboring windows; and
- an optional screen anchor.

The weighted mean becomes the edge's next unconstrained target. A damping factor prevents oscillation when multiple relationships compete.

## 4. Projection / hard constraints

After every relaxation iteration, the unconstrained rectangle is projected back into usability constraints:

- ordinary edge movement budget;
- maximum resize percentage;
- minimum width and height;
- fixed-size windows retain their size;
- monitor work-area bounds when off-screen rescue is enabled.

Off-screen recovery is intentionally different from cosmetic optimization. It is a hard correctness constraint and may exceed the ordinary movement budget.

## 5. Why this is different from Classic

Classic mode performs a sequence of local rules. A later rule can partially undo an earlier rule, and each threshold is essentially independent.

Smart mode solves all inferred relationships together. A three-window arrangement such as one large window above two smaller windows naturally produces a small constraint graph with shared horizontal/vertical boundaries. Competing requests are resolved by weight rather than execution order.

## 6. User-facing intent profiles

The mathematical parameters above are **implementation details**, not Smart-mode UI controls. Smart exposes only three compact intent axes, each with three levels:

- **Tidy strength — Gentle / Balanced / Assertive**: controls how strongly the optimizer may depart from the observed arrangement overall.
- **Hit tendency — Cautious / Balanced / Sensitive**: controls how readily nearby geometry is admitted into the relationship graph.
- **Size tendency — Preserve size / Balanced / Expand usage**: controls the trade-off between translating whole windows and resizing them to make better use of nearby free space.

The default is Balanced on all three axes. A profile resolver maps these choices to calibrated internal weights, inference ranges, iteration counts and hard cosmetic budgets before `SmartTidySolver` runs.

This separation is intentional:

- Smart users describe **intent**, not solver coefficients.
- Internal calibration can evolve without changing the public UI model.
- Legacy/raw numeric values in settings cannot silently alter Smart behavior.
- Classic remains the explicit expert/threshold mode for users who actually want direct pixel and percentage controls.

Off-screen rescue is kept as a separate behavior switch because it is a correctness policy, not a cosmetic optimization preference.

## 7. Research lineage

The solver is inspired by constrained graph-layout adjustment and rectangle-overlap-removal work where a layout is modified to satisfy separation constraints while staying close to the original placement. Relevant work includes Tim Dwyer, Kim Marriott and Peter J. Stuckey's fast node overlap removal / separation-constraint methods, as well as later stress-based overlap-removal methods.

NeatWin is not a direct implementation of those papers. Desktop windows add different constraints: resizability, monitor work areas, focus, Z-order visibility, size budgets and the requirement not to surface hidden background windows.

## 8. Planned extensions

The current Smart solver deliberately stops before semantic or learned inference. Useful future extensions include:

- shared-boundary clustering so three or more windows can converge onto one inferred line as a group;
- explicit inequality separation constraints for larger-but-still-unintentional overlaps;
- aspect/proportion priors for near-equal splits and repeated rows/columns;
- per-window minimum-track-size queries (`WM_GETMINMAXINFO`-equivalent behavior where feasible);
- a preview/debug overlay showing inferred relationships and confidence before applying a plan;
- solver diagnostics that record energy before/after and the constraints responsible for every changed rectangle.
