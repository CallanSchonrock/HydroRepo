from qgis.core import (
    QgsVectorLayer,
    QgsCoordinateReferenceSystem,
    QgsCoordinateTransform,
    QgsCoordinateTransformContext
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
    sourceCrs = subcatLayer.crs()
    targetCrs = QgsCoordinateReferenceSystem('EPSG:4326')
    coord_transform = QgsCoordinateTransform(sourceCrs, targetCrs, QgsCoordinateTransformContext())
    centroids = {}
    areas = {}
    for feature in subcatLayer.getFeatures():
        geom = feature.geometry()
        centroid = coord_transform.transform(geom.centroid().asPoint())
        x = centroid.x()
        y = centroid.y()
        centroids[str(feature.attributes()[0])] = [x,y]
        areas[str(feature.attributes()[0])] = feature["Area"]
    return centroids, areas
        
        
def convert_to_RORB(outputPath, subcatsFile, vecFile):
    
    centroids, areas = get_centroids(subcatsFile)
    subcats, subbyDS, prints, lengths, slopes = get_junctions(vecFile)
    finalOutlets = 0
    with open(outputPath, 'w') as f:
        f.write("HydroRepo\n")
        f.write("C RORB_GE 6.45\n")
        f.write("C WARNING - DO NOT EDIT THIS FILE OUTSIDE RORB TO ENSURE BOTH GRAPHICAL AND CATCHMENT DATA ARE COMPATIBLE WITH EACH OTHER\n")
        f.write("C THIS FILE CANNOT BE OPENED IN EARLIER VERSIONS OF RORB GE - CURRENT VERSION IS v6.45\n")
        f.write("C \n")
        f.write("C HydroRepo\n")
        f.write("C\n")
        f.write("C #FILE COMMENTS\n")
        f.write("C   0\n")
        f.write("C \n")
        f.write("C #SUB-AREA AREA COMMENTS\n")
        f.write("C   0\n")
        f.write("C \n")
        f.write("C #IMPERVIOUS FRACTION COMMENTS\n")
        f.write("C   0\n")
        f.write("C \n")
        f.write("C #BACKGROUND IMAGE\n")
        f.write("C  T  F\n")
        f.write("C\n")
        f.write("C #NODES\n")
        
        subcats.sort(key=int)
        f.write(f"C{str(len(subcats)*2).rjust(7)}\n")
        
        #---------------------------------------Write Centroids------------------------------------------
        for subcat in subcats:
            line = "C"
            line += subcat.rjust(7)
            line += str(round(centroids[subcat][0],3)).rjust(15)
            line += str(round(centroids[subcat][1],3)).rjust(15)
            line += "1.000".rjust(15)
            line += "1 0".rjust(4)
            line += str(int(subcat) + len(subcats)).rjust(6)
            line += str(areas[subcat]).rjust(33 - len(' ' + str(subcat)))
            line += "0.000".rjust(15)
            line += "0".rjust(3)
            line += "  0  1"
            line += '\n'
            line += f"C"
            line += '\n'
            f.write(line)
            
        
        #----------------------------------------Write Junctions------------------------------------------
        for subcat in subcats:
            line = "C"
            line += (str(int(subcat) + len(subcats))).rjust(7)
           
            if (subcat in subbyDS):
                line += str(round((centroids[subcat][0] + centroids[subbyDS[subcat]][0]) / 2,3)).rjust(15)
                line += str(round((centroids[subcat][1] + centroids[subbyDS[subcat]][1]) / 2,3)).rjust(15)
                line += "1.000".rjust(15)
                line += "0 0".rjust(4)
                line += str(subbyDS[subcat]).rjust(6)
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
                line += str(round((centroids[subcat][0] + sum(deltax)/len(deltax)/2),3)).rjust(15)
                line += str(round((centroids[subcat][1] + sum(deltay)/len(deltay)/2),3)).rjust(15)
                line += "1.000".rjust(15)
                line += "0 1".rjust(4)
                line += str(0).rjust(6)
            
            line += "0.000000".rjust(33 - len(' ' + str(subcat)))
            line += "0.000".rjust(15)
            if (subcat in prints):
                line += "0".rjust(3)
                line += "  0  0"
                line += '\n'
                line += f"C  Inflow {subcat}"
            else:
                line += "0".rjust(3)
                line += "  0  1"
                line += '\n'
                line += f"C"
                
            line += '\n'
            f.write(line)
        
        #-------------------------------------------Write Centroid to Junction Reaches-----------------------------------------
        f.write("C\n")
        f.write("C #REACHES\n")
        f.write("C" + str(len(subcats)*2-finalOutlets).rjust(7) + "\n")
        
        count = 1
        for subcat in subcats:
            line = "C"
            line += str(count).rjust(7)
            line += str(int(subcat)).rjust(27)
            line += str(int(subcat) + len(subcats)).rjust(6)
            line += "              0 1 0"
            line += str(lengths[subcat]).rjust(15)
            line += str(slopes[subcat]).rjust(15)
            line += "     1  0"
            line += '\n'
            line += 'C'
            if (subcat in subbyDS):
                line += str(round((centroids[subcat][0] + ((centroids[subbyDS[subcat]][0] + centroids[subcat][0])/2))/2,3)).rjust(16) + '\n'
                line += 'C'
                line += str(round((centroids[subcat][1] + ((centroids[subbyDS[subcat]][1] + centroids[subcat][1])/2))/2,3)).rjust(16) + '\n'
            else:
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
                line += str(round((centroids[subcat][0] + (centroids[subcat][0]+ sum(deltax)/len(deltax)/2))/2,3)).rjust(16) + '\n'
                line += 'C'
                line += str(round((centroids[subcat][1] + (centroids[subcat][1] + sum(deltay)/len(deltay)/2))/2,3)).rjust(16) + '\n'
                            
            
            f.write(line)
            count += 1
        #-------------------------------------------Write Centroid to Junction Reaches-----------------------------------------
        for subcat in subcats:
            if not subcat in subbyDS:
                count += 1
                continue
            line = "C"
            line += str(count).rjust(7)
            line += str(int(subcat) + len(subcats)).rjust(27)
            line += subbyDS[subcat].rjust(6)
            line += "              0 1 0"
            line += str(lengths[subbyDS[subcat]]).rjust(15)
            line += str(slopes[subbyDS[subcat]]).rjust(15)
            line += "     1  0"
            line += '\n'
            line += 'C'
            line += str(round((centroids[subbyDS[subcat]][0] + ((centroids[subbyDS[subcat]][0] + centroids[subcat][0])/2))/2,3)).rjust(16) + '\n'
            line += 'C'
            line += str(round((centroids[subbyDS[subcat]][1] + ((centroids[subbyDS[subcat]][1] + centroids[subcat][1])/2))/2,3)).rjust(16) + '\n'

            
            f.write(line)
            count += 1
            
        f.write("C\n")
        f.write("C #STORAGES\n")
        f.write("C      0\n")
        f.write("C\n")
        f.write("C #INFLOW/OUTFLOW\n")
        f.write("C      0\n")
        f.write("C\n")
        f.write("C END RORB_GE\n")
            

# convert_to_RORB(r"F:\scripts\Mydro\Examples\EPR\Model\RORB\rorb.catg",
                # r"R:\Jobs\24020147_Logan_Flood_Study_2024_Flagstone_Creek\Analysis\CSIM\003_export\Flagstone_03-SubCatchments.shp",
                # r"R:\Jobs\24020147_Logan_Flood_Study_2024_Flagstone_Creek\Analysis\CSIM\003_export\URBS\FLAG_03.vec")