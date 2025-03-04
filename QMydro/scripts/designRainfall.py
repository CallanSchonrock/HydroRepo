import requests
import json
import csv
import zipfile
import os
from math import log10
from qgis.core import *
import time
import subprocess
from bs4 import BeautifulSoup


def scrapeData(lon, lat, dirname):
    # Scrape ARR Datahub https://data.arr-software.org/
    response = requests.get(f"https://data.arr-software.org/?lon_coord={lon}&lat_coord={lat}&type=json&All=1")
    json_data = json.loads(response.content.decode('utf-8'))
    with open(os.path.join(dirname,"data.txt"), 'w') as arrData:
        json.dump(json_data, arrData, indent=4)
    results = json_data['layers']
    
    return results
    
def getPreburst(initialLoss, continuingLoss, prebursts, formattedAeps, durs, dirname, lat, lon, pnb = None):
    
    preburstDurations = [60, 90, 120, 180, 360, 720, 1080, 1440, 2160, 2880, 4320]
    longerDurs = [5760,7200,8640,10080]
    
    with open(os.path.join(dirname, "initialLosses.csv"), 'w', newline='') as lossesFile:
        writer = csv.writer(lossesFile)
        writer.writerow([f"! IL: {initialLoss} CL: {continuingLoss}"])
        writer.writerow(["Duration", "063", "050", "039", "020", "010", "005", "002", "001", "200", "500", "1000", "2000"])
        if pnb is None:
            for index, durPreburst in enumerate(prebursts):
                sixtyThree = (((durPreburst[0] - durPreburst[1]) / (0.5 - 0.2)) * (0.63 - 0.5)) + durPreburst[0]
                thirtyNine = (((durPreburst[0] - durPreburst[1]) / (0.5 - 0.2)) * (0.39 - 0.2)) + durPreburst[1]
                durPreburst = [sixtyThree, durPreburst[0], thirtyNine] + durPreburst[1:] + [initialLoss,initialLoss,initialLoss,initialLoss]
                if index == 0:
                    interpGradient = [(initialLoss - preburst) / 60 for preburst in durPreburst]
                    for dur in durs[:6]:
                        writer.writerow([dur] + [round(min(max(gradient * dur,0),initialLoss),1) for gradient in interpGradient])
                elif index == 4:
                    interpGradient = [(max(initialLoss - durPreburst[preburst],0) - previousDurs[preburst]) / 180 for preburst in range(len(durPreburst))]
                    writer.writerow([270] + [round(min(max((interpGradient[gradient] * 90 + previousDurs[gradient]),0),initialLoss),1) for gradient in range(len(interpGradient))])
                    
                previousDurs = [round(min(max(initialLoss - preburst,0),initialLoss),1) for preburst in durPreburst]
                writer.writerow([preburstDurations[index]] + previousDurs)
        else:
            for index, durIl in enumerate(pnb):
                sixtyThree = (((durIl[0] - durIl[1]) / (0.5 - 0.2)) * (0.63 - 0.5)) + durIl[0]
                thirtyNine = (((durIl[0] - durIl[1]) / (0.5 - 0.2)) * (0.39 - 0.2)) + durIl[1]
                durIl = [sixtyThree, durIl[0], thirtyNine] + durIl[1:] + [0,0,0,0]
                if index == 0:
                    interpGradient = [il / 60 for il in durIl]
                    for dur in durs[:6]:
                        writer.writerow([dur] + [round(min(max(gradient * dur,0),initialLoss),1) for gradient in interpGradient])
                elif index == 4:
                    interpGradient = [(durIl[il] - previousDurs[il]) / 180 for il in range(len(durIl))]
                    writer.writerow([270] + [round(min(max(interpGradient[gradient] * 90 + previousDurs[gradient],0),initialLoss),1) for gradient in range(len(interpGradient))])
                    
                previousDurs = [round(min(max(il,0),initialLoss),1) for il in durIl]
                writer.writerow([preburstDurations[index]] + previousDurs)
                
        for dur in longerDurs:
            writer.writerow([dur,initialLoss, initialLoss, initialLoss, initialLoss, initialLoss, initialLoss, initialLoss, initialLoss, 0, 0, 0, 0])
        
    with open(os.path.join(dirname, "XXX_IL_PreBurst_001.trd"), 'w', newline='') as preburstData:
        writer = csv.writer(preburstData)
        writer.writerow(["! TUFLOW READ FILE - SETTING RAINFALL LOSS VARIABLES"])
        writer.writerow([f"! GENERATED BY QMydro SCRIPT USING ARR DATAHUB PARAMETERS AT: {lat} {lon}"])
        writer.writerow([f"! ARR IL: {initialLoss} ARR CL: {continuingLoss}"])
        writer.writerow([f"Set Variable CL_1 == {continuingLoss}"])
        
        
        
        with open(os.path.join(dirname,"initialLosses.csv"), 'r') as csvfile:
            reader = csv.reader(csvfile)
            next(reader)
            next(reader)
            conditional_duration = "If"
            header = True
            for rows in reader:
                writer.writerow([f"{conditional_duration} Event == {int(rows[0]):03d}m"])
                conditional_duration = "Else If"
                
                writer.writerow([f"    If Event == 063"])
                writer.writerow([f"        Set Variable IL_1 == {rows[1]}"])
                
                for index, ils in enumerate(rows[2:]):
                    writer.writerow([f"    Else If Event == {formattedAeps[index + 1]}"])
                    writer.writerow([f"        Set Variable IL_1 == {ils}"])
                writer.writerow(["    Else"])
                writer.writerow(["        Pause == Event Not Recognised"])
                writer.writerow(["    End If"])
                
        writer.writerow(["Else"])
        writer.writerow(["    Pause == Event Not Recognised"])
        writer.writerow(["End If"])


def getARF(aeps, durs, area, arfParams, dirname):
    a = float(arfParams["a"])
    b = float(arfParams["b"])
    c = float(arfParams["c"])
    d = float(arfParams["d"])
    e = float(arfParams["e"])
    f = float(arfParams["f"])
    g = float(arfParams["g"])
    h = float(arfParams["h"])
    i = float(arfParams["i"])
    areaForCalc = max(area, 10)
    arfs = {}
    
    for aep in aeps:
        for dur in durs:
            if dur <= 720:
                arfs[(aep, dur)] = min(1, 1.0 - 0.287 * (areaForCalc ** 0.265 - 0.439 * log10(dur)) * dur ** (-0.36) + \
                                   2.26 * 10.0 ** (-3.0) * areaForCalc ** 0.226 * dur ** 0.125 * (0.3 + log10(aep)) + \
                                   0.0141 * areaForCalc ** 0.213 * 10.0 ** ((-0.021 * (dur - 180) ** 2.0) / 1440) * (0.3 + log10(aep)))
            elif dur < 1440:
                shortDurARF = min(1, 1.0 - 0.287 * (areaForCalc ** 0.265 - 0.439 * log10(720)) * 720 ** (-0.36) + \
                              2.26 * 10.0 ** (-3.0) * areaForCalc ** 0.226 * 720 ** 0.125 * (0.3 + log10(aep)) + \
                              0.0141 * areaForCalc ** 0.213 * 10.0 ** ((-0.021 * (720 - 180) ** 2.0) / 1440) * (0.3 + log10(aep)))
                
                longDurARF = min(1,1.0 - a * (areaForCalc ** b - c * log10(1440)) * 1440 ** (-d) + \
                             e * areaForCalc ** f * 1440 ** g * (0.3 + log10(aep)) + \
                             h * 10.0 ** (i * areaForCalc * (1440 / 1440.0)) * (0.3 + log10(aep)))
                
                arfs[(aep, dur)] = shortDurARF + (longDurARF - shortDurARF) * (dur - 720)/720
            else:
                arfs[(aep, dur)] = min(1, 1.0 - a * (areaForCalc ** b - c * log10(dur)) * dur ** (-d) + \
                                   e * areaForCalc ** f * dur ** g * (0.3 + log10(aep)) + \
                                   h * 10.0 ** (i * areaForCalc * (dur / 1440.0)) * (0.3 + log10(aep)))
    
    if area < areaForCalc:
        arfs = {key: 1 - 0.6614*(value)*(area**0.4 - 1) for key, value in arfs.items()}
    arfs = {key: round(value,5) for key, value in arfs.items()}
    
    with open(os.path.join(dirname,"ARFs.csv"), 'w', newline='') as arfFile:
        writer = csv.writer(arfFile)
        writer.writerow(["Duration"] + aeps)
        for dur in durs:
            writer.writerow([dur] + [arfs[(aep, dur)] for aep in aeps])

    return arfs


def get_ifds(lon, lat, durs, dirname, aeps):
    bomDurs = [1,2,3,4,5,10,15,20,25,30,45,60,90,120,180,270,360,540,720,1080,1440,1800,2160,2880,4320,5760,7200,8640,10080]
    durAeps = {}
    frequencies = ["very_frequent", "ifds", "rare"]
    for freq in frequencies:
        for dur in bomDurs:
            durAeps[(freq, dur)] = []
    for rarity in frequencies:
        url = f'http://www.bom.gov.au/water/designRainfalls/revised-ifd/?design={rarity}&sdmin=true&sdhr=true&sdday=true&coordinate_type=dd&latitude={lat}&longitude={lon}&user_label=&values=depths&update=&year=2016'
        headers = {
            'Accept': 'text/html,application/xhtml+xml,application/xml;q=0.9,image/webp,  image/apng,*/*;q=0.8,application/signed-exchange;v=b3',
            'Accept-Encoding': 'gzip', 'Accept-Language': 'en-US,en;q=0.9,es;q=0.8', 'Upgrade-Insecure-Requests': '1',
            'User-Agent': 'Mozilla/5.0 (Macintosh; Intel Mac OS X 10_12_6) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/76.0.3809.132 Safari/537.36'}
        response = requests.get(url, headers=headers)
        # Check if the request was successful (status code 200)
        if response.status_code == 200:
            with open('ifds.html', 'wb') as file:
                file.write(response.content)
        
        # Read the HTML file
        with open('ifds.html', 'r', encoding='utf-8') as file:
            html_content = file.read()
        os.remove('ifds.html')
        
        # Parse the HTML content using BeautifulSoup
        soup = BeautifulSoup(html_content, 'html.parser')
        
        # Find the desired table (replace 'your_table_id' with the actual ID or class of your table)
        table = soup.find('table', {'id': 'depths'})
        # Iterate over rows in the table and write them to the CSV file
        for index, row in enumerate(table.find_all('tr')):
            if index <= 6:
                continue
            entryVals = [col.get_text(strip=True) for col in row.find_all(['th', 'td'])]
            if "winter" in entryVals[0] and len(entryVals) > 0:
                continue
               
            durAeps[(rarity, bomDurs[index - 2])].extend(entryVals[1:])
        
        ifds = {}
        with open(os.path.join(dirname,"IFD.csv"), 'w', newline='') as ifdFiles:
            writer = csv.writer(ifdFiles)
            writer.writerow(["Dur"] + aeps)
            for dur in durs:
                ifds[dur] = durAeps[("ifds", dur)][:2] + [durAeps[("very_frequent", dur)][5]] + durAeps[("ifds", dur)][2:] + durAeps[("rare", dur)][1:]
                writer.writerow([dur] + ifds[dur])
    return ifds



def get_temporalPatterns(tpUrl, arealUrl, area, ifds, arfs, aeps, formattedAeps, durs, dirname):
    
    if os.path.exists(os.path.join(dirname, "Temporal_Patterns")):
        filesToRemove = []
        for files in os.listdir(os.path.join(dirname, "Temporal_Patterns")):
            filesToRemove.extend([files])
        for files in filesToRemove:
            os.remove(os.path.join(dirname, "Temporal_Patterns", files))
    
    response = requests.get("https://data.arr-software.org/" + tpUrl)
    
    with open(os.path.join(dirname,'temporalPatterns.zip'), 'wb') as zip_file:
        zip_file.write(response.content)
    
    with zipfile.ZipFile(os.path.join(dirname, 'temporalPatterns.zip'), 'r') as zip_ref:
        zip_ref.extractall(os.path.join(dirname,'Temporal_Patterns'))
        
    os.remove(os.path.join(dirname,'temporalPatterns.zip'))
    
    response = requests.get("https://data.arr-software.org/" + arealUrl)
    
    with open(os.path.join(dirname,'temporalPatterns.zip'), 'wb') as zip_file:
        zip_file.write(response.content)
    
    with zipfile.ZipFile(os.path.join(dirname, 'temporalPatterns.zip'), 'r') as zip_ref:
        zip_ref.extractall(os.path.join(dirname,'Temporal_Patterns'))
        
    os.remove(os.path.join(dirname,'temporalPatterns.zip'))
    
    if not os.path.exists(os.path.join(dirname, "RF")):
        os.makedirs(os.path.join(dirname, "RF"))
    
    for files in os.listdir(os.path.join(dirname, "Temporal_Patterns")):
        if files[-14:] == "Increments.csv":
            if "Areal" in files:
                arealPatternFile = files
            else:
                temporalPatternFile = files
    
    frequent_tps = {}
    intermediate_tps = {}
    rare_tps = {}
    arealDurs = [720,1080,1440,2160,2880,4320,5760,7200,8640,10080]
    with open(os.path.join(dirname, "Temporal_Patterns", temporalPatternFile), 'r') as csvfile:
        reader = csv.reader(csvfile)
        next(reader)
        for rows in reader:
            data = [float(row) for row in rows[5:] if len(row) > 0]
            if int(rows[1]) in arealDurs and area >= 75:
                continue
            if not int(rows[1]) in durs:
                continue
            if rows[4] == "frequent":
                if rows[1] in frequent_tps:
                    frequent_tps[rows[1]].append(data)
                else:
                    frequent_tps[rows[1]] = [rows[2],data]
            if rows[4] == "intermediate":
                if rows[1] in intermediate_tps:
                    intermediate_tps[rows[1]].append(data)
                else:
                    intermediate_tps[rows[1]] = [rows[2],data]
            if rows[4] == "rare":
                if rows[1] in rare_tps:
                    rare_tps[rows[1]].append(data)
                else:
                    rare_tps[rows[1]] = [rows[2],data]
    if area >= 75:
        arealAreas = [100,200,500,1000,2500,5000,10000,20000,40000]
        closest_area = min(arealAreas, key=lambda x: abs(x - area))
        with open(os.path.join(dirname, "Temporal_Patterns", arealPatternFile), 'r') as csvfile:
            reader = csv.reader(csvfile)
            next(reader)
            for rows in reader:
                data = [float(row) for row in rows[5:] if len(row) > 0]
                if int(rows[4]) != closest_area:
                    continue
                if not int(rows[1]) in durs:
                    continue
                if rows[1] in frequent_tps:
                    frequent_tps[rows[1]].append(data)
                else:
                    frequent_tps[rows[1]] = [rows[2],data]
                if rows[1] in intermediate_tps:
                    intermediate_tps[rows[1]].append(data)
                else:
                    intermediate_tps[rows[1]] = [rows[2],data]
                if rows[1] in rare_tps:
                    rare_tps[rows[1]].append(data)
                else:
                    rare_tps[rows[1]] = [rows[2],data]
        
    for dur in durs:
        for index, formattedAep in enumerate(formattedAeps):
            with open(os.path.join(dirname, "RF", f"RF_{formattedAep}_{dur:03d}m.csv"), 'w', newline='') as f:
                writer = csv.writer(f)
                writer.writerow(["! Written by Qhydro Python Script"])
                writer.writerow(["Time (hour)", "E0", "E1", "E2", "E3", "E4", "E5", "E6", "E7", "E8", "E9"])
                rainfall = float(ifds[dur][index]) * arfs[(aeps[index],dur)]/100
                timeStep = float(frequent_tps[str(dur)][0])/60
                time = 0
                writer.writerow([time,0,0,0,0,0,0,0,0,0,0])
                time += timeStep
                for i in range(len(frequent_tps[str(dur)][1])):
                    if int(formattedAep) >= 100:
                        writer.writerow([round(time,4)] + [round((float(rare_tps[str(dur)][tp + 1][i]) * rainfall),4) for tp in range(len(rare_tps[str(dur)][1:]))])
                    elif int(formattedAep) >= 10:
                        writer.writerow([round(time,4)] + [round((float(frequent_tps[str(dur)][tp + 1][i]) * rainfall),4) for tp in range(len(frequent_tps[str(dur)][1:]))])
                    elif int(formattedAep) <= 1:
                        writer.writerow([round(time,4)] + [round((float(rare_tps[str(dur)][tp + 1][i]) * rainfall),4) for tp in range(len(rare_tps[str(dur)][1:]))])
                    elif int(formattedAep) < 10:
                        writer.writerow([round(time,4)] + [round((float(intermediate_tps[str(dur)][tp + 1][i]) * rainfall),4) for tp in range(len(intermediate_tps[str(dur)][1:]))])
                    time += timeStep
                writer.writerow([round(time,4),0,0,0,0,0,0,0,0,0,0])
    
    
def getRainfallData(lon, lat, useARF, area, dirname):
    durs = [10,15,20,25,30,45,60,90,120,180,270,360,540,720,1080,1440,1800,2160,2880,4320,5760,7200,8640,10080]
    aeps = [0.63, 0.5, 0.39, 0.2, 0.1, 0.05, 0.02, 0.01, 0.005, 0.002, 0.001, 0.0005]
    formattedAeps = ["063", "050", "039", "020", "010", "005", "002", "001", "200", "500", "1000", "2000"]
    
    results = scrapeData(lon, lat, dirname)
    initialLoss = results["StormLosses"]["Storm Initial Losses (mm)"]
    continuingLoss = results["StormLosses"]["Storm Continuing Losses (mm/h)"]
    pnb = None
    try:
        pnb = results['BurstIL']['data']
    except:
        pass
    getPreburst(initialLoss, continuingLoss, results['Preburst50']['data'], formattedAeps, durs, dirname, lat, lon, pnb)
    if useARF:
        arfs = getARF(aeps, durs, area, results["ARFParams"], dirname)
    else:
        arfs = {}
        for aep in aeps:
            for dur in durs:
                arfs[(aep, dur)] = 1
        with open(os.path.join(dirname,"ARFs.csv"), 'w', newline='') as arfFile:
            writer = csv.writer(arfFile)
            writer.writerow(["Duration"] + aeps)
            for dur in durs:
                writer.writerow([dur] + [1.0 for aep in aeps])
    ifds = get_ifds(lon, lat, durs, dirname, aeps)
    temporalPatterns = get_temporalPatterns(results['PointTP']['url'],results['ArealTP']['url'], area, ifds, arfs, aeps, formattedAeps, durs, dirname) 

# getRainfallData(152.93223,-27.79944,True,55.56753,r"R:\Jobs\24020147_Logan_Flood_Study_2024_Flagstone_Creek\Analysis\URBS\design\ifd\Rainfall")