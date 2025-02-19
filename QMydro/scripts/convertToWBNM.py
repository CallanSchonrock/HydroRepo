from qgis.core import (
    QgsVectorLayer,
)
import csv

def get_junctions(vecFile):
    subcatsDs = {}
    masterSubcats = []
    lengths = {}
    slopes = {}
    prints = []
    with open(vecFile, 'r') as csvfile:
        reader = csv.reader(csvfile)
        subcats = []
        lastSubcat = None
        for rows in reader:
            if not len(rows) > 0:
                continue
            if "print" in rows[0].lower():
                prints.append(lastSubcat)
                continue
            if any(substring in rows[0].lower() for substring in ["rain", "add rain", "route"]):
                subcat = rows[0].split('#')[1].split()[0]
                lastSubcat = subcat
                if subcat not in masterSubcats:
                    masterSubcats.insert(0,subcat)
                if "rain" in rows[0].lower():
                    lengths[subcat] = rows[0].split()[rows[0].split().index('L')+2]
                    slopes[subcat] = rows[0].split()[rows[0].split().index('Sc')+2]
                    subcats.insert(0,subcat)
                if "route" in rows[0].lower():
                    for subby in subcats:
                        if type(subby) is str:
                            subcatsDs[subby] = subcat
                    subcats = [subby for subby in subcats if type(subby) is not str]
            
            
            if "get." in rows[0].lower():
                subbies = []
                get = True
                for subcat in subcats:
                    if type(subcat) is list and get:
                        get = False
                        subbies.extend(subcat)
                    else:
                        subbies.extend([subcat])
                subcats = subbies
                
            if "store." in rows[0].lower():
                subcats = [subcats]
                
    return masterSubcats, subcatsDs, prints, lengths, slopes
        
def get_centroids(subcatsFile):
    subcatLayer = QgsVectorLayer(subcatsFile, "Subbies")
    centroids = {}
    areas = {}
    for feature in subcatLayer.getFeatures():
        geom = feature.geometry()
        centroid = geom.centroid().asPoint()
        x = centroid.x()
        y = centroid.y()
        centroids[str(feature.attributes()[0])] = [x,y]
        areas[str(feature.attributes()[0])] = feature["Area"]
    return centroids, areas
        
        
def convert_to_WBNM(outputPath, subcatsFile, vecFile):
    
    centroids, areas = get_centroids(subcatsFile)
    subcats, subbyDS, prints, lengths, slopes = get_junctions(vecFile)
    finalOutlets = 0
    subcats.reverse()
    min_x = 9999999999
    max_x = -9999999999
    min_y = 9999999999
    max_y = -9999999999
    
    for key, centroid in centroids.items():
        min_x = min(min_x, centroid[0])
        max_x = max(max_x, centroid[0])
        min_y = min(min_y, centroid[1])
        max_y = max(max_y, centroid[1])
    
    with open(outputPath, 'w') as f:
        f.write("#####START_PREAMBLE_BLOCK##########|###########|###########|###########|\n")
        f.write("RUNFILETEMPLATE.wbn\n")
        f.write("This file was created by QMydro available at https://hydrorepo.com\n")
        f.write("\n")
        f.write("\n")
        f.write("\n")
        f.write("\n")
        f.write("\n")
        f.write("\n")
        f.write("#####END_PREAMBLE_BLOCK############|###########|###########|###########|\n")
        f.write("\n")
        f.write("\n")
        f.write("#####START_STATUS_BLOCK############|###########|###########|###########|\n")
        f.write(f"{outputPath}\n")
        f.write("INSERT DATE\n")
        f.write("INSERT ORGANISATION\n")
        f.write("INSERT VERSION\n")
        f.write("#####END_STATUS_BLOCK##############|###########|###########|###########|\n")
        f.write("\n")
        f.write("\n")
        f.write("#####START_DISPLAY_BLOCK###########|###########|###########|###########|\n")
        f.write(str(round(min_x,2)).rjust(12) + str(round(min_y,2)).rjust(12) + str(round(max_x,2)).rjust(12) + str(round(max_y,2)).rjust(12) + '\n')
        f.write("No GIS filename provided! \n")
        f.write("Note        ! No GIS model as yet - add details here as it develops      \n")
        f.write("#####END_DISPLAY_BLOCK#############|###########|###########|###########|\n")
        
        f.write("\n")
        f.write("\n")
        f.write("#####START_TOPOLOGY_BLOCK##########|###########|###########|###########|\n")
        f.write(f"{str(len(subcats)).rjust(12)}      Catchment Name\n")
        for subcat in subcats:
            line = ""
            line += "SUB" + subcat
            line += " " * (24 - len(line) - len(str(round(centroids[subcat][0],2))))
            line += str(round(centroids[subcat][0],2))
            line += str(round(centroids[subcat][1],2)).rjust(12)
            if (subcat in subbyDS):
                line += str(round((centroids[subcat][0] + centroids[subbyDS[subcat]][0]) / 2,2)).rjust(12)
                line += str(round((centroids[subcat][1] + centroids[subbyDS[subcat]][1]) / 2,2)).rjust(12)
                line += ("SUB" + str(subbyDS[subcat])).rjust(12)
            else:
                finalOutlets += 1
                deltax = []
                deltay = []
                for subby in subcats:
                    if subby in subbyDS:
                        if subbyDS[subby] == subcat:
                            deltax.append(centroids[subcat][0] - centroids[subby][0])
                            deltay.append(centroids[subcat][1] - centroids[subby][1])
                if len(deltax) == 0:
                    deltax.append(float(lengths[subcat]))
                    deltay.append(float(lengths[subcat]))
                line += str(round((centroids[subcat][0] + sum(deltax)/len(deltax)/2),2)).rjust(12)
                line += str(round((centroids[subcat][1] + sum(deltay)/len(deltay)/2),2)).rjust(12)
                line += "SINK".rjust(12)
            line += "\n"
            f.write(line)
        f.write("#####END_TOPOLOGY_BLOCK############|###########|###########|###########|\n")
        
        f.write("\n")
        f.write("\n")
        f.write("#####START_SURFACES_BLOCK##########|###########|###########|###########|\n")
        f.write("        0.77         1.5         0.1         1.0                         \n")
        f.write("       -1.00                                                             \n")
        for subcat in subcats:
            line = ""
            line += "SUB" + subcat
            line += " " * (24 - len(line) - len(f"{round(float(areas[subcat])*100,3)}"))
            line += f"{round(float(areas[subcat])*100,3)}"
            line += f"{round(0,5)}".rjust(12)
            line += f"1.0".rjust(12)
            line += "\n"
            f.write(line)
        f.write("#####END_SURFACES_BLOCK############|###########|###########|###########|\n")
        
        f.write("\n")
        f.write("\n")
        
        f.write("#####START_FLOWPATHS_BLOCK#########|###########|###########|###########|\n")
        f.write(f"{str(len(subcats)).rjust(12)}\n")
        for subcat in subcats:
            f.write(f"SUB{subcat}\n")
            f.write(f"#####ROUTING\n")
            f.write(f"        1.00\n")
        f.write(f"#####END_FLOWPATHS_BLOCK###########|###########|###########|###########|\n")
        
        f.write("\n")
        f.write("\n")
       
        f.write("#####START_LOCAL_STRUCTURES_BLOCK##|###########|###########|###########|\n")
        f.write("           0 \n")
        f.write("#####END_LOCAL_STRUCTURES_BLOCK####|###########|###########|###########|\n")
        
        f.write("\n")
        f.write("\n")
        
        f.write("#####START_OUTLET_STRUCTURES_BLOCK#|###########|###########|###########|\n")
        f.write("           0 \n")
        f.write("#####END_OUTLET_STRUCTURES_BLOCK###|###########|###########|###########|\n")
        
        f.write("\n")
        f.write("\n")
        
        f.write("#####START_STORM_BLOCK#############|###########|###########|###########|\n")
        f.write("           2\n")
        f.write("#####START_STORM#1\n")
        f.write("1% AEP DES storm spectrum with full ARR DFE calcs for all subareas\n")
        f.write("        5.00\n")
        f.write("       10.00\n")
        f.write("#####START_DESIGN_RAIN_ARR\n")
        f.write("         1.0          -1          -1        -1.0          -1\n")
        f.write("IFD_DATA_IN_GAUGE_FILES\n")
        f.write("           3\n")
        f.write("Gauge1\n")
        f.write("Gauge2\n")
        f.write("Gauge3\n")
        f.write("PAT_DATA_IN_REGION_FILE\n")
        f.write("myRegion_Increments.csv\n")
        f.write("CAT_DATA_IN_CATCHMENT_FILE\n")
        f.write("myHUBcatchment.txt\n")
        f.write("#####END_DESIGN_RAIN_ARR\n")
        f.write("#####START_CALC_RAINGAUGE_WEIGHTS\n")
        f.write("#####END_CALC_RAINGAUGE_WEIGHTS\n")
        f.write("#####START_LOSS_RATES\n")
        f.write("ARRLOSSES\n")
        f.write("#####END_LOSS_RATES\n")
        f.write("#####START_RECORDED_HYDROGRAPHS\n")
        f.write("           0\n")
        f.write("#####END_RECORDED_HYDROGRAPHS\n")
        f.write("#####START_IMPORTED_HYDROGRAPHS\n")
        f.write("           0\n")
        f.write("#####END_IMPORTED_HYDROGRAPHS\n")
        f.write("#####END_STORM#1\n")
        f.write("#####START_STORM#2\n")
        f.write("A historic storm  recorded at 2 gauges in January  2005\n")
        f.write("        2.00\n")
        f.write("       20.00\n")
        f.write("#####START_RECORDED_RAIN\n")
        f.write("01/01/2005\n")
        f.write("21:15\n")
        f.write("           8       20.00\n")
        f.write("MM/HOUR\n")
        f.write("           2\n")
        f.write("RAIN_GAUGE_1\n")
        f.write(str(round(min_x,2)).rjust(12) + str(round(min_y,2)).rjust(12) + "\n")
        f.write("        6.00\n")
        f.write("       14.00\n")
        f.write("       28.00\n")
        f.write("       15.00\n")
        f.write("       22.00\n")
        f.write("       18.00\n")
        f.write("        8.00\n")
        f.write("        5.00\n")
        f.write("RAIN_GAUGE_2\n")
        f.write(str(round(max_x,2)).rjust(12) + str(round(max_y,2)).rjust(12) + "\n")
        f.write("       12.00\n")
        f.write("       10.00\n")
        f.write("       30.00\n")
        f.write("       18.00\n")
        f.write("       18.00\n")
        f.write("       16.00\n")
        f.write("        4.00\n")
        f.write("        2.00\n")
        f.write("#####END_RECORDED_RAIN\n")
        f.write("#####START_CALC_RAINGAUGE_WEIGHTS\n")
        f.write("#####END_CALC_RAINGAUGE_WEIGHTS\n")
        f.write("#####START_LOSS_RATES\n")
        f.write("GLOBAL               5.0         2.0\n")
        f.write("#####END_LOSS_RATES\n")
        f.write("#####START_RECORDED_HYDROGRAPHS\n")
        f.write("        0\n")
        f.write("#####END_RECORDED_HYDROGRAPHS\n")
        f.write("#####START_IMPORTED_HYDROGRAPHS\n")
        f.write("        0\n")
        f.write("#####END_IMPORTED_HYDROGRAPHS\n")
        f.write("#####END_STORM#2\n")
        f.write("#####END_STORM_BLOCK####################################################\n")
        
        
        
# convert_to_RORB(r"E:\scripts\Mydro\Examples\EPR\Model\WBNM\wbnm.wbn",
                # r"R:\Jobs\24020147_Logan_Flood_Study_2024_Flagstone_Creek\Analysis\CSIM\003_export\Flagstone_03-SubCatchments.shp",
                # r"R:\Jobs\24020147_Logan_Flood_Study_2024_Flagstone_Creek\Analysis\CSIM\003_export\URBS\FLAG_03.vec")