"""Trace the user-supplied Marin screenshots into a fixed coastline/road graph.

The screenshots are the source of the shape. No random coastline or generated
street loops are used. Coordinates below are in pixels of screenshot 1.
"""
import json, os, sys
from pathlib import Path
sys.path.insert(0, str(Path(os.environ['TEMP'])/'open-territory-map-trace-deps'))
import cv2
import numpy as np

ROOT=Path(__file__).resolve().parents[1]
images=[cv2.imread(str(ROOT/'AssetLibrary/ReferenceScreens'/f'{i}.png')) for i in range(1,5)]
transforms=[[[1,0,0],[0,1,0]],[[.571672,-.000159,13.140],[.000159,.571672,227.238]],[[.572466,0,603.931],[0,.572466,50.258]],[[.572466,0,917.641],[0,.572466,27.929]]]
canvas=np.zeros((735,1467,3),np.uint8)
for image,transform in zip(images,transforms):
    matrix=np.array(transform,dtype=np.float32)
    warped=cv2.warpAffine(image,matrix,(1467,735))
    mask=cv2.warpAffine(np.ones(image.shape[:2],np.uint8),matrix,(1467,735))
    canvas[mask>0]=warped[mask>0]
canvas[:28,1011:]=canvas[28:29,1011:]
hsv=cv2.cvtColor(canvas,cv2.COLOR_BGR2HSV)
water=cv2.inRange(hsv,np.array([83,80,50]),np.array([118,255,255]))
land=((water==0)&(canvas.max(axis=2)>30)).astype(np.uint8)*255
land=cv2.morphologyEx(land,cv2.MORPH_CLOSE,np.ones((3,3),np.uint8))
contours,_=cv2.findContours(land,cv2.RETR_EXTERNAL,cv2.CHAIN_APPROX_SIMPLE)
outline=cv2.approxPolyDP(max(contours,key=cv2.contourArea),1.15,True)[:,0,:].tolist()
roads=[]
def road(name,kind,width,points):roads.append(dict(id=name,kind=kind,width=width,points=points))
# Highway 101: southern bridge, western bend, long inland carriageways.
road('101-south','highway',16,[(410,735),(417,671),(426,595),(435,525),(442,466),(448,401),(452,344),(456,313),(467,292),(493,277),(520,268),(546,260)])
road('101-inland','highway',15,[(546,260),(595,264),(655,268),(717,272),(776,276),(837,279),(883,283),(915,290)])
road('101-express','highway',14,[(915,290),(944,284),(970,269),(990,247),(1024,230),(1063,216),(1106,205),(1150,198),(1196,193),(1223,191),(1261,174),(1286,177),(1305,191),(1311,208),(1308,229)])
road('east-bridge','highway',16,[(1108,244),(1154,235),(1201,232),(1241,232),(1280,235),(1322,244),(1384,263),(1467,283)])
road('east-ramp-north','ramp',6,[(1194,230),(1229,222),(1264,224),(1295,231),(1322,244),(1384,263)])
road('east-ramp-south','ramp',6,[(1149,249),(1191,254),(1240,267),(1277,273),(1300,269),(1309,251),(1308,229)])
# Coast and the inward-facing Horseshoe Bay. Every junction is shared.
road('headlands-access','street',6,[(213,455),(246,434),(277,444),(304,454),(331,457),(342,450),(350,431),(359,404),(367,382),(380,369),(391,367),(408,376),(421,397),(429,423),(437,449)])
road('west-hill-outer','street',6,[(391,367),(405,358),(431,342),(452,331),(474,334),(490,350),(506,377),(521,396),(538,414),(558,426),(580,422)])
road('west-hill-inner','street',5,[(437,449),(426,463),(410,461),(404,447),(410,420),(420,410),(434,416),(437,435),(437,449)])
road('bay-west-loop','street',6,[(452,331),(469,310),(498,301),(525,300),(548,304),(565,306),(593,314),(614,330),(622,350),(622,365)])
road('west-shop-loop','street',5,[(474,334),(481,327),(490,328),(497,337),(509,354),(520,377),(521,387),(513,394),(504,392),(495,381),(489,363),(480,346),(474,334)])
road('west-cross','street',5,[(497,337),(518,326),(546,321),(566,320),(583,322),(600,321),(614,330)])
road('bay-west-descent','street',6,[(525,300),(536,327),(536,356),(542,383),(539,406),(528,425),(508,430),(484,430),(454,419)])
road('bay-front-west','street',7,[(437,497),(466,485),(493,479),(526,472),(553,465),(571,451),(580,422),(586,396),(595,377),(608,368),(622,365),(640,373),(650,398),(652,425),(664,443),(686,452),(711,437)])
road('inland-to-bay','street',7,[(596,265),(621,274),(646,286),(669,297),(688,314),(695,338),(697,366),(711,402),(727,437),(740,474),(753,499),(778,509),(811,508),(844,502),(872,488),(881,474),(885,450),(890,426),(900,411)])
road('waterfront','street',8,[(711,437),(725,480),(743,506),(769,519),(802,520),(838,514),(870,500),(892,483),(901,458),(901,436),(900,411),(915,398),(940,389),(964,381),(990,370),(1012,361),(1036,346),(1055,327),(1066,302),(1076,281),(1092,268),(1108,262),(1134,257),(1154,253),(1162,239)])
road('bay-front-cut','street',5,[(725,480),(756,469),(785,463),(812,457),(829,443),(831,418),(825,402),(833,394),(845,396),(850,409),(849,433),(846,450),(831,456),(812,457)])
road('bay-mid-link','street',5,[(652,425),(680,409),(697,398),(714,385),(734,373),(752,373),(784,384),(805,390),(825,389),(839,380),(846,361),(839,345),(827,335)])
road('highway-mid-ramp','ramp',6,[(915,290),(903,300),(901,311),(909,323),(920,336),(934,351),(939,373),(933,387),(915,398)])
road('town-spine','street',7,[(900,411),(899,392),(902,373),(913,357),(931,345),(954,337),(978,330),(1000,316),(1013,300),(1019,283),(1037,265),(1064,255),(1082,256),(1092,268)])
road('town-west-cross','street',5,[(885,450),(915,440),(930,419),(935,396),(940,389)])
road('town-low-cross','street',5,[(901,458),(925,450),(951,435),(975,421),(999,408),(1017,394),(1025,374),(1036,346)])
road('town-center-cross','street',5,[(900,411),(923,405),(951,399),(976,389),(1000,381),(1012,361)])
road('town-upper-cross','street',5,[(913,357),(936,368),(955,376),(964,381)])
road('town-diagonal','street',5,[(954,337),(966,351),(982,361),(990,370)])
road('town-short','street',5,[(978,330),(1000,337),(1019,335),(1037,325),(1055,327)])
road('town-upper-block','street',5,[(1000,316),(1027,311),(1051,302),(1066,302)])
road('town-bridge-loop','street',5,[(1037,265),(1063,277),(1082,276),(1094,260),(1108,244),(1114,226),(1106,205)])
road('marina-street','street',6,[(1076,281),(1100,284),(1124,279),(1138,267),(1134,257)])
road('east-port-street','street',6,[(1154,253),(1182,269),(1211,278),(1240,281),(1271,279),(1300,269)])
road('east-headland','street',5,[(1261,174),(1261,147),(1267,130),(1280,124),(1293,130),(1299,146),(1297,161),(1286,177)])
# Footpaths on the headlands and the branched hillside network.
road('west-cape-trail','trail',2,[(57,542),(79,535),(99,541),(117,535),(141,519),(166,502),(188,489),(211,475),(238,458),(254,438),(248,426),(239,423),(227,436),(216,452),(213,467),(225,480),(242,484),(272,477),(300,481),(307,498),(303,517),(279,526),(262,544),(277,557),(293,563),(297,581),(304,602),(331,615),(360,625),(392,650),(416,652)])
road('west-cape-loop','trail',2,[(211,475),(216,456),(227,435),(239,423),(262,427),(285,441),(304,454),(277,444),(246,434),(238,458),(225,480)])
road('cape-park-loop','trail',2,[(262,544),(237,541),(230,525),(240,511),(257,506),(279,507),(303,517)])
road('upper-west','trail',2,[(458,285),(437,250),(442,231),(474,218),(513,185),(559,168),(596,144),(624,126),(650,126),(682,137),(718,149),(756,158),(793,173),(831,158),(865,150),(905,149),(947,147),(981,148),(1007,154),(1037,154),(1065,151),(1085,155),(1104,169),(1128,180),(1167,181),(1203,188),(1215,200),(1212,211)])
road('hillside-lower','trail',2,[(442,231),(482,237),(521,239),(559,244),(604,249)])
road('hillside-west-spur','trail',2,[(596,144),(621,167),(647,196),(665,213),(704,215),(742,222),(753,240),(768,252),(786,281)])
road('hillside-west-loop','trail',2,[(669,136),(682,154),(689,175),(710,183),(730,194),(741,213),(762,207),(775,195),(766,178),(745,166),(719,161),(682,154)])
road('hillside-mid','trail',2,[(786,281),(767,259),(770,248),(787,243),(814,243),(827,238),(831,218),(832,191),(831,158)])
road('hillside-back','trail',2,[(793,173),(816,179),(832,191),(871,187),(908,178),(936,163),(957,154),(981,148)])
road('hillside-top-loop','trail',2,[(831,158),(868,165),(908,167),(936,163),(968,162),(994,164),(1020,170),(1047,177),(1075,180),(1104,169)])
road('hillside-house','trail',2,[(988,232),(976,204),(974,192),(984,183),(1000,181),(1022,186),(1047,197),(1063,216)])
road('hillside-to-town','trail',2,[(1203,188),(1179,193),(1158,192),(1139,189),(1116,192),(1098,197),(1077,198)])
road('bay-hill-descent','trail',2,[(786,281),(799,310),(817,331),(827,335),(827,351),(817,369),(805,390)])

layout=dict(id='marin-reference-v2',metersPerPixel=2.4,origin=[750,650],size=[1467,735],coast=outline,roads=roads,
            source='Four user-supplied screenshots, aligned using SIFT/RANSAC; fixed road control points',
            alignment=transforms,spawn=[1009,351],harbor=[1230,280],plaza=[974,366])
(ROOT/'Map/reference-layout.json').write_text(json.dumps(layout,ensure_ascii=False,indent=2),encoding='utf-8')
preview=canvas.copy()
cv2.polylines(preview,[np.array(outline,dtype=np.int32)],True,(20,40,245),2)
for r in roads:cv2.polylines(preview,[np.array(r['points'],dtype=np.int32)],False,(235,60,20) if r['kind']=='trail' else (30,30,230),1)
cv2.imwrite(str(ROOT/'Reports/reference-map/traced-overlay.png'),preview)
cv2.imwrite(str(ROOT/'Reports/reference-map/reference-mosaic.png'),canvas)
print(f'{len(outline)} coastline anchors; {len(roads)} fixed road/trail traces')
