"""Generate the fictional Professor Quacks egg dataset (deterministic).

Story: inspectors send an egg to the JUMBO carton (label 1) when it is heavy and
wide enough to sit snugly in the big cups; long, skinny eggs wobble in jumbo cups
and go to STANDARD (label 0) even when heavy. The data is synthetic and fictional.

Output CSV columns: id,mass_g,length_mm,width_mm,label,split
Usage: python3 -I tools/gen_eggs.py <out.csv>
"""
import math, random, sys

SEED = 20261107
N = 200
N_TEST = 50
FLIP_RATE = 0.05        # inspector disagreement, only near the boundary
FLIP_BAND = 0.6         # |hidden score| below this can be flipped

def generate():
    rng = random.Random(SEED)
    eggs = []
    for i in range(N):
        length = rng.gauss(58.0, 3.2)
        width = rng.gauss(43.5, 1.9) + 0.22 * (length - 58.0)
        density = rng.gauss(1.0, 0.035)
        mass = 0.000525 * length * width * width * density   # ~ellipsoid volume
        eggs.append([mass, length, width])
    # standardise for the hidden rule
    cols = list(zip(*eggs))
    mu = [sum(c) / N for c in cols]
    sd = [math.sqrt(sum((x - m) ** 2 for x in c) / N) for c, m in zip(cols, mu)]
    rows = []
    for i, (m, L, W) in enumerate(eggs):
        zm, zL, zW = ((m - mu[0]) / sd[0], (L - mu[1]) / sd[1], (W - mu[2]) / sd[2])
        score = 1.6 * zm + 1.4 * zW - 1.5 * zL - 0.25
        label = 1 if score > 0 else 0
        if abs(score) < FLIP_BAND and rng.random() < FLIP_RATE / 0.25:
            label = 1 - label
        rows.append((i, round(m, 1), round(L, 1), round(W, 1), label))
    idx = list(range(N)); rng.shuffle(idx)
    test = set(idx[:N_TEST])
    return [(r[0], r[1], r[2], r[3], r[4], "test" if r[0] in test else "train") for r in rows]

if __name__ == "__main__":
    rows = generate()
    with open(sys.argv[1], "w") as f:
        f.write("id,mass_g,length_mm,width_mm,label,split\n")
        for r in rows:
            f.write("%d,%.1f,%.1f,%.1f,%d,%s\n" % r)
