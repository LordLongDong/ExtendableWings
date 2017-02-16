using System;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;
using System.Text;
using System.Threading;
using System.ComponentModel;
using System.Runtime.Remoting.Messaging;


namespace ExtendableWing
{
    public class ExtendableWing : ModuleLiftingSurface
    {

        #region KSP GUI

        // Field to toggle the Extension Status
        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "Wing Extension")
        UI_Toggle(disabledText = "Retracted", enabledText = "Extended")]
        public bool extended = false;

        // Field to toggle the Auto Extend status
        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "Auto-Extend")
        UI_Toggle(disabledText= "Disabled", enabledText= "Enabled")]
        public bool autoExtend = false;

        // Adjustable field for setting the auto-extend range
        [KSPField(isPersistant = true, guiActiveEditor = true, guiActive = true, guiName = "Auto-Extend Speed", guiFormat = "F0", guiUnits = "m/s"),
        UI_FloatRange(maxValue = 300f, minValue = 0f, scene = UI_Scene.All, stepIncrement = 1f)]
        public float extendSpeed = 100;

        // Action group to toggle the extension
        [KSPAction("Toggle Auto-Extend")]
        public void toggleAEAction(KSPActionParam param)
        {autoExtend = !autoExtend;}

        // Action group to toggle the extension
        [KSPAction("Toggle Extension")]
        public void toggleAction(KSPActionParam param)
        {extended = !extended;}

        // Action group to Extend the extension
        [KSPAction("Extend Wing")]
        public void extendAction(KSPActionParam param)
        {extended = true;}

        // Action group to Retract the extension
        [KSPAction("Retract Wing")]
        public void retractAction(KSPActionParam param)
        {extended = false;}

        // Variable to determine time to wait for animation to play
        [KSPField(isPersistant = false)]
        public float WaitForAnimation = 0;

        #endregion

        #region Variables

        // Variables to store color strings for indicator lights
        private Color green = new Vector4(0F, 1, 0.23F, 1);
        private Color yellow = new Vector4(1,0.43F,0F,1);
        private Color red = new Vector4(1, 0, 0.06F, 1);
        private Color clear = Color.clear;

        // Variable to store the part's original surface area
        private float defaultSurfaceArea = 1.0F;
        
        // Field for displaying the current speed
        public double speed;

        // Variable to determine if we are passing our threshold
        public bool triggerExtend = false;

        // Variable to determine if we are passing our threshold
        public bool triggerRetract = false;

        // Variable to store our speed on the last update
        public double lastSpeed;

        // Keep track of the last state of the wing extension
        public bool lastState = false;

        // Variable to let us know when the animation has been activated once
        public bool initialized = false;

        // Variable to let us know when the GUI has been updated once
        public bool runOnce = true;

        // Set the name of the animation which controls the wing extension
        public string AnimationName = "Toggle Extend";

        // Variable to set which indicator light should be overwritten for use by this module
        public string indicatorToReplace = "Airlock";

        // Variable for the extension animation
        private ModuleAnimateGeneric mainAnimation = null;

        // Variable for the control surface
        private ModuleControlSurface controlSurface = null;

        // Variable for the lifting surface
        private ModuleLiftingSurface liftingSurface = null;

        // Variable for determining if a part is a Command part
        private bool isCommand = false;

        // List of command pods on the active vessel
        private List<Part> commandPods = new List<Part>();

        // Variable to track current part
        private Part p;

        // List of indicator panels on the vessel
        List<InternalIndicatorPanel> panels = new List<InternalIndicatorPanel>();

        // List of lights on the indicator panels for this vessel
        List<InternalIndicatorPanel.Indicator> lights = new List<InternalIndicatorPanel.Indicator>();

        // Variable to toggle depending on control surface status
        private bool isCtrl = false;

        // Variable to define how many seconds it should take to transition from retracted lift to extended lift
        private int transitionTime = 4;

        // Variable to define how much additional lift (%) an extended control surface adds
        private float ctrlLiftAdd = 0.75F;

        // Variable to define how much additional lift (%) an extended rigid surface adds
        private float regLiftAdd = 0.20F;



        #endregion

        #region Override OnLoad
        public override void OnLoad(ConfigNode node)
        {
            // Find the control surface
            controlSurface = this.part.FindModuleImplementing<ModuleControlSurface>();

            if (controlSurface != null)
            {
                // If the part is a control surface, change the part info
                isCtrl = true;
            }

            base.OnLoad(node);
        }
        #endregion

        #region OnStart

        public override void OnStart(StartState state)
        {
            // Find the control surface
            controlSurface = this.part.FindModuleImplementing<ModuleControlSurface>();

            // Find the lifting surface
            liftingSurface = this.part.FindModuleImplementing<ModuleLiftingSurface>();

            // Find the main animation
            mainAnimation = part.FindModulesImplementing<ModuleAnimateGeneric>().SingleOrDefault();
                  
          

            
            if (runOnce)
            {
                runOnce = false;

                // Disable the built-in ModuleAnimateGeneric Button
                List<ModuleAnimateGeneric> animList = part.FindModulesImplementing<ModuleAnimateGeneric>();
                foreach (ModuleAnimateGeneric anim in animList)
                {
                    anim.Fields["status"].guiActive = false;
                    anim.Fields["status"].guiActiveEditor = false;
                }

                // Grab the default part values for lift and surfaceArea
                if (controlSurface != null)
                {
                    // If the part is a control surface, get that default value
                    defaultSurfaceArea = controlSurface.deflectionLiftCoeff;
                }
                else if(liftingSurface != null)
                {
                    // If the part is a non-control surface, get that default value
                    defaultSurfaceArea = liftingSurface.deflectionLiftCoeff;
                }

                // Zero out the extendable wing module's default values
                deflectionLiftCoeff = 0;

             

            }


            base.OnStart(state);
        }

        #endregion

        #region LateUpdate

        public void LateUpdate()
        {


            // Quickly activate and reset the animation so the control surfaces can function normally
            if(!initialized) // Only do this once
            {
                initializeAnimation(AnimationName);
            }

            // Locate all the assets we'll need to use indicator lights on this vessel
            if (commandPods.Count < 1) // Only do this once
            {
                // Locate and Store all Command Pod Parts on this Vessel if not already located
                if (this.vessel != null)
                {
                    commandPods = discoverPartsWithModule("ModuleCommand");
                }

                // For each command pod lets look for indicator panels
                foreach (Part x in commandPods)
                {
                    // Only interested in Manned Command Modules
                    int mincrew = x.Modules.GetModule<ModuleCommand>().minimumCrew;
                    if (mincrew > 0)
                    {
                        // Make a list of all the lights on this indicator panel
                        List<InternalIndicatorPanel.Indicator> allLights = findIndicatorsOnPart(x);

                        // For each light, test to see if it's the one we're supposed to use
                        foreach (InternalIndicatorPanel.Indicator thislight in allLights)
                        {
                            if (thislight.value.ToString() == indicatorToReplace)
                            {
                                // If so, add it to the list of lights to toggle for this module's status
                                lights.Add(thislight);
                            }
                        }
                    }
                }
             }


            // Handle auto-extend triggers
            if (autoExtend)
            {
                if (triggerExtend) { extended = true; }
                else { extended = false; }
            }

            // Toggle the wing extension if requested
            if(extended != lastState)
            {
                lastState = !lastState;
                // Create a new worker thread for extending the wing so we don't freeze up the game
                toggleWingExtensionDelegate worker = new toggleWingExtensionDelegate(toggleWingExtension);

                // Create an asyncronous task to run on our new thread
                System.ComponentModel.AsyncOperation async = AsyncOperationManager.CreateOperation(null);

                // Define a callback (in case we want to do something special after the method is complete)
                AsyncCallback completedCallback = new AsyncCallback(MyTaskCompletedCallback);

                // Kick off the wing extension
                worker.BeginInvoke(extended, completedCallback, async);

            }

            // Update our current speed
            if (autoExtend) { updateSpeed(); }
            
        }

        #endregion

        #region GetInfo Override

        // Override the method which generates part information in the VAB/SPH
        public override string GetInfo()
        {
            var info = new StringBuilder();

            if (isCtrl)
                info.AppendLine("Additional Lift: " + (ctrlLiftAdd * 100) +"%");
            else
                info.AppendLine("Additional Lift: " + (regLiftAdd * 100) + "%");
                info.AppendLine("Auto-Extend: Up to 300 m/s");

            return info.ToString();
        }
        #endregion

        #region Custom Functions

        // Method to handle results from our async lift calculations
        private void MyTaskCompletedCallback(IAsyncResult ar)
        {
            // Get the original worker delegate and the AsyncOperation instance
            toggleWingExtensionDelegate worker =(toggleWingExtensionDelegate)((AsyncResult)ar).AsyncDelegate;
            System.ComponentModel.AsyncOperation async = (System.ComponentModel.AsyncOperation)ar.AsyncState;

            // Finish the asynchronous operation
            worker.EndInvoke(ar);
        }

        // Method to manually play animations
        public void Play_Anim(string aname, float aspeed, float atime)
        {
            try
            {
                Animation anim;
                Animation[] animators = part.FindModelAnimators(aname);
                if (animators.Length > 0)
                {
                    anim = animators[0];
                    anim.clip = anim.GetClip(AnimationName);
                    anim[aname].speed = aspeed;
                    anim[aname].normalizedTime = atime;
                    anim[aname].layer = 1;
                    anim.Play(aname);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError("Exception in Play_Anim");
                Debug.LogError("[" + AnimationName + "]  Err: " + ex);
            }
        }

        // Method to discover all parts of a certain type and store them
        private List<Part> discoverPartsWithModule(String modToFind)
        {
            List<Part> found = new List<Part>();
            if (this.vessel != null)
            {
                // Loop through every part on the vessel
                for (int i = 0; i < this.vessel.parts.Count; i++)
                {
                    isCommand = false;
                    if (vessel.parts[i] != null)
                    {
                        p = vessel.parts[i];

                        // Determine if the part has ModuleCommand
                        foreach (PartModule pmod in p.Modules)
                        {
                            // If we find the desired module, set the bool to true
                            if (pmod.moduleName == modToFind)
                            {
                                isCommand = true;
                            }
                        }
                    }

                    // If bool is true add this to the part array
                    if (isCommand) { found.Add(p); }
                }
            }
            return found;
        }

        // Method to find all the indicators on a part
        private List<InternalIndicatorPanel.Indicator> findIndicatorsOnPart(Part thisPart)
        {
            // Create new array to hold indicators to add as we find them
            List<InternalIndicatorPanel.Indicator> foundLights = new List<InternalIndicatorPanel.Indicator>();

            // Loop through each prop on the part to find Indicator Panels
            foreach (InternalProp prop in thisPart.internalModel.props)
            {
                if (prop != null)
                {
                    if (prop.name == "IndicatorPanelFlaps")
                    {
                        // Found an Indicator Panel. Lets get a list of the lights on that panel and store it for later
                        InternalIndicatorPanel thisPanel = prop.FindModelComponent<InternalIndicatorPanel>();
                        
                        
                        if (thisPanel != null)
                        {
                            // Lets discover all the lights on this particular panel
                            foreach (InternalIndicatorPanel.Indicator thisLight in thisPanel.indicators.list)
                            {
                                if (thisLight != null)
                                {
                                    // Add the light to the list
                                    foundLights.Add(thisLight);
                                }
                            }

                        }
                    }
                }
            }
            
            return foundLights;
        }

        // Method to Turn on or off Indicator Lights (on/off , color to make light , list of lights to apply this to)
        private void toggleIndicators(bool turnOn, Color lightColor, List<InternalIndicatorPanel.Indicator> lightsList)
        {
            
            if (lightsList != null)
            {
                foreach (InternalIndicatorPanel.Indicator light in lightsList)
                {
                    if (light != null)
                    {
                       
                        if (turnOn)
                        {
                            light.colorOn = lightColor;
                            light.SetOn();
                        }
                        else
                        {
                           
                            light.SetOff();
                        }
                    }
                }
            }
        }

        // Method to gather the current speed
        private void updateSpeed()
        {
            // Save our new speed for use next time
            var oldSpeed = this.speed;

            // Grab the vessel's speed
            try
            {
                if (this.vessel != null)
                {
                    this.speed = this.vessel.srf_velocity.magnitude;
                }
            }
            catch { }

            if (!(Double.IsNaN(this.speed)))
            {
                // Save our new speed for use next time
                if (Double.IsNaN(this.lastSpeed))
                {
                    this.lastSpeed = this.speed;
                    return;
                }

                // Calculate the threshold
                var lowerBound = Math.Min(this.speed, this.lastSpeed);
                var upperBound = Math.Max(this.speed, this.lastSpeed);

                // Activate if requirements are met
                this.triggerRetract = this.extendSpeed < lowerBound;
                this.triggerExtend = this.extendSpeed > upperBound;
            }
            // Save our last known speed for use next time
            this.lastSpeed = oldSpeed;
        }

        // Method to gather the extension state of all extendable parts
        private int vesselExtensionStatus ()
        {
            // 0 = No Extended Parts, 1 = Some Extended Parts, 2 = All Parts Extended
            int status = 0;

            // Total number of extendable parts
            int total = 0;

            // Number of extendable parts which are extended
            int count = 0;

            // Get a complete list of vessel parts
            foreach (Part p in FlightGlobals.ActiveVessel.Parts)
                foreach (PartModule pm in p.Modules)
                    if (pm is ExtendableWing)
                    {
                        total++;
                        bool partExtended = ((ExtendableWing)pm).extended;
                        if (partExtended)
                            count++;
                    }
            // Determine the state of all extendable parts
            if (count < 1)
                status = 0;
            else if (count < total)
                status = 1;
            else if (count == total)
                status = 2;
            else status = 0;

            return status;
        }

        // Method to quickly initialize an animate generic so other animations on the part can function properly
        private void initializeAnimation(String NameOfAnimation)
        {
            Animation extendanim = part.FindModelAnimators(NameOfAnimation)[0];
            extendanim.Play();
            extendanim.Stop();
            extendanim.Rewind();
            initialized = true;
        }

        // Method delegate to extend/retract the wings async (so while we run the ui doesn't pause)
        private delegate void toggleWingExtensionDelegate(bool extend);

        // Method to toggle wing extension
        private void toggleWingExtension(bool extend)
        {
            // Extend
            if (extend)
            {

                // Extend the wing animation
                Play_Anim(AnimationName, 1.5f, -1.0f);
            

                // Turn on Indicator Lights
                if (vesselExtensionStatus() == 2)
                    toggleIndicators(true, red, lights);
                else
                    toggleIndicators(true, green, lights);


                // Update values for the control surface deflection
                if (controlSurface != null)
                {
                    // Log the current time
                    var startTime = DateTime.UtcNow;

                    // Set current time multiplier
                    float x = 0;
                    float interval = ctrlLiftAdd / transitionTime;

                    while (DateTime.UtcNow - startTime < TimeSpan.FromSeconds(transitionTime))
                    {
                        // Slow the execution down so we don't stress the game
                        Thread.Sleep(500);

                        // Ensure we don't add too much lift
                        if (x > ctrlLiftAdd)
                            x = ctrlLiftAdd;

                        // Increase the lift by x
                        controlSurface.deflectionLiftCoeff = defaultSurfaceArea + (defaultSurfaceArea * x);

                        // Add more lift to x for next time
                        x += interval;
                    }

                    // All done, let's make sure we got to the final value
                    controlSurface.deflectionLiftCoeff = defaultSurfaceArea + (defaultSurfaceArea * ctrlLiftAdd);
                }
                else if (liftingSurface != null)
                {
                    // Log the current time
                    var startTime = DateTime.UtcNow;

                    // Set current time multiplier
                    float x = 0;
                    float interval = regLiftAdd / transitionTime;

                    while (DateTime.UtcNow - startTime < TimeSpan.FromSeconds(transitionTime))
                    {
                        // Slow the execution down so we don't stress the game
                        Thread.Sleep(500);

                        // Ensure we don't add too much lift
                        if (x > regLiftAdd)
                            x = regLiftAdd;

                        // Increase the lift by x
                        liftingSurface.deflectionLiftCoeff = defaultSurfaceArea + (defaultSurfaceArea * x);

                        // Add more lift to x for next time
                        x += interval;
                    }

                    // All done, let's make sure we got to the final value
                    liftingSurface.deflectionLiftCoeff = defaultSurfaceArea + (defaultSurfaceArea * regLiftAdd);

                }



            }
            // Retract
            else
            {
                // Retract the wing animation
                Play_Anim(AnimationName, -1.0f, 1.0f);

                // Turn off Indicator Lights
                if (vesselExtensionStatus() == 1)
                    toggleIndicators(true, green, lights);
                else
                    toggleIndicators(false, clear, lights);

                // Update values for the control surface deflection
                if (controlSurface != null)
                {
                    // Log the current time
                    var startTime = DateTime.UtcNow;

                    float interval = (defaultSurfaceArea *ctrlLiftAdd) / transitionTime;

                    while (DateTime.UtcNow - startTime < TimeSpan.FromSeconds(transitionTime))
                    {
                        // Slow the execution down so we don't stress the game
                        Thread.Sleep(500);

                        // Decrease the lift by x
                        controlSurface.deflectionLiftCoeff -= interval;

                        // Ensure we don't remove too much lift
                        if (controlSurface.deflectionLiftCoeff < defaultSurfaceArea)
                            controlSurface.deflectionLiftCoeff = defaultSurfaceArea;

                    }

                    // All done, let's make sure we got to the final value
                    controlSurface.deflectionLiftCoeff = defaultSurfaceArea;

                }
                else if (liftingSurface != null)
                {

                    // Log the current time
                    var startTime = DateTime.UtcNow;

                    float interval = (regLiftAdd * defaultSurfaceArea) / transitionTime;

                    while (DateTime.UtcNow - startTime < TimeSpan.FromSeconds(transitionTime))
                    {
                        // Slow the execution down so we don't stress the game
                        Thread.Sleep(500);

                        // Decrease the lift by x
                        liftingSurface.deflectionLiftCoeff -= interval;

                        // Ensure we don't remove too much lift
                        if (liftingSurface.deflectionLiftCoeff < defaultSurfaceArea)
                            liftingSurface.deflectionLiftCoeff = defaultSurfaceArea;

                    }

                    // All done, let's make sure we got to the final value
                    liftingSurface.deflectionLiftCoeff = defaultSurfaceArea;
                }

            }
        }

        #endregion
    }
}
