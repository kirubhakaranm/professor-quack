"""Reference implementation of the Quacks classifier (mirrors Assets/_Quack/Core).

Used to cross-check the C# core: same CSV, same standardisation, same loss,
same gradient-descent update. Usage: python3 -I tools/ref_model.py <eggs.csv>
"""
import math, sys

LAMBDA = 0.01  # L2 on weights only (not bias)

def load(path):
    rows = []
    with open(path) as f:
        next(f)
        for line in f:
            i, m, L, W, y, s = line.strip().split(",")
            rows.append(((float(m), float(L), float(W)), int(y), s))
    return rows

def scaler(X):
    n = len(X); d = len(X[0])
    mu = [sum(x[j] for x in X) / n for j in range(d)]
    sd = [math.sqrt(sum((x[j] - mu[j]) ** 2 for x in X) / n) for j in range(d)]
    return mu, sd

def standardise(X, mu, sd):
    return [[(x[j] - mu[j]) / sd[j] for j in range(len(mu))] for x in X]

def sigmoid(z):
    if z >= 0: return 1 / (1 + math.exp(-z))
    e = math.exp(z); return e / (1 + e)

def bce(z, y):
    return max(z, 0) - z * y + math.log1p(math.exp(-abs(z)))

def objective(w, b, X, Y, lam=LAMBDA):
    n = len(X)
    data = sum(bce(sum(wi * xi for wi, xi in zip(w, x)) + b, y) for x, y in zip(X, Y)) / n
    return data + 0.5 * lam * sum(wi * wi for wi in w)

def gradient(w, b, X, Y, lam=LAMBDA):
    n = len(X); g = [0.0] * len(w); gb = 0.0
    for x, y in zip(X, Y):
        e = sigmoid(sum(wi * xi for wi, xi in zip(w, x)) + b) - y
        for j in range(len(w)): g[j] += e * x[j]
        gb += e
    return [gj / n + lam * wj for gj, wj in zip(g, w)], gb / n

def train(X, Y, lr, steps, w=None, b=0.0):
    w = list(w or [0.0] * len(X[0]))
    for _ in range(steps):
        g, gb = gradient(w, b, X, Y)
        w = [wj - lr * gj for wj, gj in zip(w, g)]; b -= lr * gb
    return w, b

def accuracy(w, b, X, Y):
    return sum((sum(wi * xi for wi, xi in zip(w, x)) + b >= 0) == (y == 1) for x, y in zip(X, Y)) / len(X)

if __name__ == "__main__":
    rows = load(sys.argv[1])
    tr = [r for r in rows if r[2] == "train"]; te = [r for r in rows if r[2] == "test"]
    Xtr_raw = [r[0] for r in tr]; Ytr = [r[1] for r in tr]
    Xte_raw = [r[0] for r in te]; Yte = [r[1] for r in te]
    mu, sd = scaler(Xtr_raw)
    print("mu", [round(v, 6) for v in mu], "sd", [round(v, 6) for v in sd])
    Xtr = standardise(Xtr_raw, mu, sd); Xte = standardise(Xte_raw, mu, sd)
    for name, cols in [("mass", [0]), ("mass+length", [0, 1]), ("mass+width", [0, 2]), ("all3", [0, 1, 2])]:
        sub = lambda X: [[x[c] for c in cols] for x in X]
        w, b = train(sub(Xtr), Ytr, 1.0, 2000)
        print("%-12s train acc %.3f test acc %.3f loss %.4f w %s b %.3f" % (name, accuracy(w, b, sub(Xtr), Ytr), accuracy(w, b, sub(Xte), Yte), objective(w, b, sub(Xtr), Ytr), [round(v, 3) for v in w], b))
    # reference values for C# tests
    w0 = [0.5, -0.25, 0.75]; b0 = 0.1
    print("REF objective(w0,b0) = %.12f" % objective(w0, b0, Xtr, Ytr))
    g, gb = gradient(w0, b0, Xtr, Ytr); print("REF grad(w0,b0) =", ["%.12f" % v for v in g], "%.12f" % gb)
    w, b = train(Xtr, Ytr, 0.5, 100); print("REF train lr=0.5 x100 w =", ["%.12f" % v for v in w], "b = %.12f" % b, "obj %.12f" % objective(w, b, Xtr, Ytr))
    for lr in [0.01, 0.1, 1, 5, 20, 50, 100, 200]:
        w, b = [0.0]*3, 0.0; hist = []
        for s in range(60):
            hist.append(objective(w, b, Xtr, Ytr)); w, b = train(Xtr, Ytr, lr, 1, w, b)
        print("lr %6.2f loss@10 %.3f loss@59 %.3f max %.3f" % (lr, hist[10], hist[-1], max(hist)))
