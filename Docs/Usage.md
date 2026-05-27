# Common tasks
## Running the Study
1) On the title screen, click on the Settings gear in the top right and ensure all settings are satisfactory. 
2) Ensure the trials and matrices (see trial creation) are correct. 
3) Input the participant's name, ID, and gender, and start with either the VR or Desktop option.

## Setting Up the Shadow Mocap Suit
1) From the Shadow Motion Capture System website, download the Shadow.exe software. 
2) Configure the mocap suit according to the instructions found on their website.
3) After powering on the suit, join the Shadow WiFi network. 
4) You are now ready to run the study. 

## Finding the Persistent File Location
1) On the title screen, click on the File Location button in the top right (indicated by the floppy disk icon) to open the Persistent File Location.
2) Here, you will find the following files:
    - Matrices
    - Participant Data Log CSVs
    - Raw Matrices
    - Replays
    - Trials

## Exporting Data
1) Open the Persistent File Location.
2) Open the `/Participant Data Log CSVs` folder. If exporting, copy this folder to the desired location.
    - Participants are organized into folders based on IDs. In each participant's folder, you will find individual replay files. 
    - Each replay file contains an XRI.csv file, with the headset and controller locomotion as well as general study information, as well as a Shadow.bin, containing Shadow mocap data in binary form. If running without using a mocap suit, this binary file will not contain any data. The replay also contains a copy of the settings and trials used in that study. 

## Using the Replay System
1) Open the Persistent File Location.
2) Open `/Participant Data Log CSVs` folder and find the replay file you want to view. You will know you have the correct file if the file name is prefixed with `[Replay]`.
3) Copy the folder into the `/Replays`.
4) On the title screen, select the replay of choice from the dropdown and press the begin replay button. 

## Trial Creation
1) On the title screen, press the Trial Creation button to be taken to the trial creation tool. 
2) For non-matrix map types, function parameters can be adjusted. For matrix map types, select the matrix option from the dropdown, and input the name of the matrix map as it exists in the `/Matrix` folder.
3) For manual editing:
    1) Open the Persistent File Location.
    2) Find the trial list in `/Trials` and edit accordingly.

## Matrix Creation
1) Create or programmatically generate an nxm CSV of brightness values from 0 to 255.
2) Open the Persistent File Location.
3) Insert the generated CSV in the `/Raw Matrices` folder.
4) On the title screen, hit the Convert button. 
5) Find the matrix in `/Matrices`. Open the folder and adjust the meta file as needed. 