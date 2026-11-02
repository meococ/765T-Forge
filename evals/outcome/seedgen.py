"""Generate synthetic seed drawings for the outcome eval.

All geometry is authored here (self-made, no client data). Units: mm.
Output: evals/outcome/seeds/<task>.dxf
"""
import ezdxf, json, math, os

OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "seeds")
os.makedirs(OUT, exist_ok=True)

def newdoc():
    d = ezdxf.new("R2018", setup=True)
    d.header["$INSUNITS"] = 4  # mm
    return d

def layer(d, name, color=7):
    if name not in d.layers:
        d.layers.add(name, color=color)

def save(d, name):
    p = os.path.join(OUT, name + ".dxf")
    d.saveas(p)
    print("wrote", p)

# ---------- T1 dim-floor-plan: L-shaped plan, walls + door gaps, no dims ----------
d = newdoc()
layer(d, "A-WALL", 4); layer(d, "A-DOOR", 2)
m = d.modelspace()
# Outer walls: two rects (living wing 7200x5400 + bed wing 4800x3600 offset right)
for x1,y1,x2,y2 in [(0,0,7200,0),(7200,0,7200,5400),(7200,5400,0,5400),(0,5400,0,0),
                    (7200,1200,12000,1200),(12000,1200,12000,4800),(12000,4800,7200,4800)]:
    m.add_line((x1,y1),(x2,y2), dxfattribs={"layer":"A-WALL"})
# Internal partitions (room split at x=3600 in left wing; door gap 800..1700)
for x1,y1,x2,y2 in [(3600,800,3600,5400),(800,2700,3600,2700)]:
    m.add_line((x1,y1),(x2,y2), dxfattribs={"layer":"A-WALL"})
# Door openings (gaps in partition, drawn as separate short lines to leave openings)
m.add_line((3600,0),(3600,800),dxfattribs={"layer":"A-WALL"})
m.add_line((0,2700),(800,2700),dxfattribs={"layer":"A-WALL"})
m.add_line((1700,2700),(3600,2700),dxfattribs={"layer":"A-WALL"})
save(d, "t1-dim-floor-plan")

# ---------- T2 room-annot: 3 closed room boundaries ----------
d = newdoc()
layer(d, "A-ROOM", 3); layer(d, "A-ANNO", 7)
m = d.modelspace()
rooms = {
    "LIVING":  [(0,0),(5400,0),(5400,4200),(0,4200)],
    "KITCHEN": [(5600,0),(8200,0),(8200,3000),(5600,3000)],
    "BED":     [(5600,3200),(8200,3200),(8200,5600),(5600,5600)],
}
for name, pts in rooms.items():
    m.add_lwpolyline(pts, close=True, dxfattribs={"layer":"A-ROOM"})
# wall outline for context
layer(d,"A-WALL",4)
for x1,y1,x2,y2 in [(0,0,5400,0),(5400,0,5400,4200),(5400,4200,0,4200),(0,4200,0,0),
                    (5600,0,8200,0),(8200,0,8200,5600),(8200,5600,5600,5600),(5600,5600,5600,0)]:
    m.add_line((x1,y1),(x2,y2),dxfattribs={"layer":"A-WALL"})
save(d, "t2-room-annot")
with open(os.path.join(OUT,"t2-rooms.json"),"w") as f:
    json.dump({k: {"polygon": v} for k,v in rooms.items()}, f, indent=1)

# ---------- T3 sheet-setup: model + attributed titleblock def ----------
d = newdoc()
layer(d, "A-WALL", 4); layer(d, "A-TBLK", 7); layer(d, "A-VPORT", 9)
m = d.modelspace()
# simple building outline 12000x8000
for x1,y1,x2,y2 in [(0,0,12000,0),(12000,0,12000,8000),(12000,8000,0,8000),(0,8000,0,0)]:
    m.add_line((x1,y1),(x2,y2),dxfattribs={"layer":"A-WALL"})
# Titleblock block definition with 4 attributes
blk = d.blocks.new(name="TBLK")
blk.add_lwpolyline([(0,0),(180,0),(180,40),(0,40)], close=True, dxfattribs={"layer":"A-TBLK"})
for tag,(x,y) in {"SHEET_NO":(10,25),"SHEET_NAME":(10,15),"SCALE":(110,25),"DATE":(110,15)}.items():
    blk.add_attdef(tag, insert=(x,y), dxfattribs={"height":5,"layer":"A-TBLK"})
save(d, "t3-sheet-setup")

# ---------- T4 mep-run: corridor walls + route guide ----------
d = newdoc()
layer(d, "A-WALL", 4); layer(d, "M-DUCT", 5); layer(d, "M-ANNO", 1)
m = d.modelspace()
# corridor 18000 long, 2400 wide
for x1,y1,x2,y2 in [(0,0,18000,0),(18000,0,18000,2400),(18000,2400,0,2400),(0,2400,0,0)]:
    m.add_line((x1,y1),(x2,y2),dxfattribs={"layer":"A-WALL"})
# crossing obstacles (existing pipes) as dashed-looking short lines
for x in [4500, 9000, 13500]:
    m.add_line((x,0),(x,2400),dxfattribs={"layer":"A-WALL"})
save(d, "t4-mep-run")

# ---------- T5 layer-mess: entities on wrong layers ----------
d = newdoc()
m = d.modelspace()
for ln in ["wall","WALL-NEW","walls","Dim-2","TEXT-XX","misc","0"]:
    layer(d, ln, 7)
# walls
for x1,y1,x2,y2 in [(0,0,8000,0),(8000,0,8000,5000),(8000,5000,0,5000),(0,5000,0,0)]:
    m.add_line((x1,y1),(x2,y2),dxfattribs={"layer":"wall"})
for x1,y1,x2,y2 in [(4000,0,4000,5000),(0,2500,4000,2500)]:
    m.add_line((x1,y1),(x2,y2),dxfattribs={"layer":"WALL-NEW"})
# door swing
m.add_arc((2000,0), 900, 0, 90, dxfattribs={"layer":"misc"})
m.add_line((2000,0),(2900,0),dxfattribs={"layer":"misc"})
# scattered texts
m.add_text("KITCHEN", dxfattribs={"height":300,"layer":"TEXT-XX"}).set_placement((1000,3500))
m.add_text("HALL", dxfattribs={"height":300,"layer":"TEXT-XX"}).set_placement((5500,2000))
# fake dim bits
m.add_line((0,-800),(8000,-800),dxfattribs={"layer":"Dim-2"})
m.add_text("8000", dxfattribs={"height":250,"layer":"Dim-2"}).set_placement((3600,-700))
save(d, "t5-layer-mess")

# ---------- T6 exploded-dims: fake dimensions from lines+ticks+text ----------
d = newdoc()
layer(d, "A-WALL", 4); layer(d, "A-DIMS", 2)
m = d.modelspace()
# wall run 3600 + 2400 + 3600 = 9600 total
for x1,y1,x2,y2 in [(0,0,9600,0),(9600,0,9600,3600),(9600,3600,0,3600),(0,3600,0,0)]:
    m.add_line((x1,y1),(x2,y2),dxfattribs={"layer":"A-WALL"})
# fake dimension chain along y=-600: extension lines, dimension line, ticks, texts
def fake_dim(x1,x2,y,val):
    m.add_line((x1,-100),(x1,y-200),dxfattribs={"layer":"A-DIMS"})
    m.add_line((x2,-100),(x2,y-200),dxfattribs={"layer":"A-DIMS"})
    m.add_line((x1,y),(x2,y),dxfattribs={"layer":"A-DIMS"})
    for xx in (x1,x2):
        m.add_line((xx-60,y-60),(xx+60,y+60),dxfattribs={"layer":"A-DIMS"})
    m.add_text(str(val), dxfattribs={"height":220,"layer":"A-DIMS"}).set_placement(((x1+x2)/2-200,y+80))
fake_dim(0,3600,-600,3600); fake_dim(3600,6000,-600,2400); fake_dim(6000,9600,-600,3600)
# fake overall dim
fake_dim(0,9600,-1400,9600)
save(d, "t6-exploded-dims")

# ---------- T7 sheet-set: model + 3 layouts with borders ----------
d = newdoc()
layer(d, "A-WALL", 4); layer(d, "A-TBLK", 7)
m = d.modelspace()
for x1,y1,x2,y2 in [(0,0,6000,0),(6000,0,6000,4000),(6000,4000,0,4000),(0,4000,0,0)]:
    m.add_line((x1,y1),(x2,y2),dxfattribs={"layer":"A-WALL"})
m.add_circle((3000,2000), 1200, dxfattribs={"layer":"A-WALL"})
for lname,title in [("P1-PLAN","Ground Floor Plan"),("P2-DET","Wall Detail"),("P3-ELEV","North Elevation")]:
    lo = d.layouts.new(lname)
    lo.add_lwpolyline([(10,10),(287,10),(287,200),(10,200)], close=True,
                      dxfattribs={"layer":"A-TBLK","paperspace":True})
    lo.add_text(title, dxfattribs={"height":8,"layer":"A-TBLK","paperspace":True}).set_placement((140,25))
save(d, "t7-sheet-set")

# ---------- T8 dgn-qa: the DGN fixture is checked in (binary; ezdxf cannot author DGN) ----------
assert os.path.exists(os.path.join(OUT, "t8-dgn-qa.dgn")), "seeds/t8-dgn-qa.dgn fixture missing"
print("SEEDS DONE")
