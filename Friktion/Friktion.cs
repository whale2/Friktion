using System;
using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using ModuleWheels;

namespace Friktion
{
	public class Friktion : VesselModule
	{
		public float restVelocityThreshold = 0.05f;
		public float slopeAngleThreshold = 30;
		public bool accountForBrakes = true;
		public int freezeFrames = 20;

		private bool firstTick = true;
		private bool physicsKicked = false;
		private bool freezeOnNextFrame = false;

		private Vector3d position;
		private Quaternion rotation;

		public bool onASlope = false;
		private bool slopeCached = false;
		private bool hasGroundContact = false;
		private bool needsDampening = false;

		private int partsOnSleep = 0;
		private float slopeAngle = 0.0f;
		private bool standingStill = false;
		private int freezeCount = 0;

		public Friktion ()
		{
		}

		protected override void OnStart() {

			base.OnStart();

			GameEvents.onVesselGoOffRails.Add (onVesselGoOffRails);
			GameEvents.onVesselGoOnRails.Add (onVesselGoOnRails);
			GameEvents.onVesselWillDestroy.Add (onVesselWillDestroy);
			GameEvents.onFlightReady.Add (onFlightReady);
		}

		private void onVesselGoOffRails(Vessel v) {

			if (v != this.vessel)
				return;

			print ("CGRW: onVesselGoOffRails(); vessel=" + v.name);
			physicsKicked = true;
			firstTick = true;
		}

		private void onVesselGoOnRails(Vessel v) {

			print ("CGRW: onVesselGoOnRails(); vessel=" + v.name);
			physicsKicked = false;
			firstTick = false;
		}

		private void onVesselWillDestroy(Vessel v) {

			print ("CGRW: onVesselWillDestroy(); vessel=" + v.name);
			physicsKicked = false;
			firstTick = false;
		}

		private void onFlightReady() {

			if (vessel == null || !vessel.loaded)
				return;
			print ("CGRW: onFlightReady(); vessel=" + vessel.name + 
				"; active=" + FlightGlobals.ActiveVessel.name);
			physicsKicked = false;
			freezeOnNextFrame = false;
			position = vessel.rootPart.transform.position;
			rotation = vessel.rootPart.transform.rotation;
		}

		private void FixedUpdate() {

			if (!physicsKicked) {
				if (vessel.loaded)
					print ("CGRW: physics not yet kicked");
				return;
			}

			if (Input.GetKeyDown(KeyCode.KeypadMultiply))
				printDebug();
			
			if (firstTick) {
				firstTick = false;
				print ("CGRW: physics first tick");
				if (vessel.situation != Vessel.Situations.LANDED || vessel.srfSpeed > restVelocityThreshold)
					return;
				freezeOnNextFrame = true;
				freezeCount = 0;
			}

			if (freezeOnNextFrame) {
				print ("CGRW: physics freeze; cnt=" + freezeCount);
				freeze ();
				freezeCount ++;
				if (freezeCount > freezeFrames)
					freezeOnNextFrame = false;
				return;
			}

			needsDampening = false;
			if (isStandingStill ()) { // Neutralize vessel

				standingStill = true;
				hasGroundContact = false;
				foreach (Part p in vessel.parts)
					if (p.GroundContact) {
						if (accountForBrakes && p.Modules.Contains ("ModuleWheelBrakes")) {

							// If this is landing gear, let's check is it deployed
							if (p.Modules.Contains ("ModuleWheelDeployment")) {
								ModuleWheelDeployment deployment = 
									(ModuleWheelDeployment)p.Modules ["ModuleWheelDeployment"];
								if (deployment.Position < 0.9) { // assume that at this position gear
									// is not fully deployed and grond contact
									// is due non-wheel part of gear
									hasGroundContact = true;
									break;
								}
							}


							ModuleWheelBrakes brakes = (ModuleWheelBrakes)p.Modules ["ModuleWheelBrakes"];
							if (brakes.brakeInput > 0) {
								hasGroundContact = true;
								break;
							}
						} else {
							hasGroundContact = true;
							break;
						}
							// TODO: Ask shadowimage45 if he could make brakeInput accessible in some way
							/*else if (p.Modules.Contains("KSPWheelBrakes")) {
							KSPWheelBrakes brakes = (KSPWheelBrakes)p.Modules ["ModuleWheelBrakes"];
							if (brakes.brakeInput > 0) {
								hasGroundContact = true;
								break;
							}*/
					}
				
				if (!hasGroundContact)
					return;
				damp ();
				needsDampening = true;

			} else {
				slopeCached = false; // not standing still, force recalculation of slope
				standingStill = false;
			}

		}

		private void LateUpdate() {
			if (needsDampening)
				damp ();
			if (freezeOnNextFrame)
				freeze ();
		}

		private void damp() {

			partsOnSleep = 0;
			foreach (Part p in vessel.parts) {
				if (p.Rigidbody != null && !p.Modules.Contains ("MuMechToggle")) {
					p.Rigidbody.Sleep ();
					p.Rigidbody.velocity = Vector3.zero;
					p.Rigidbody.angularVelocity = Vector3.zero;
					partsOnSleep++;
				}
			}
		}

		private bool isStandingStill() {

			//print ("DBG: isStandingStill");
			if (!(vessel.situation == Vessel.Situations.LANDED || 
				vessel.situation == Vessel.Situations.PRELAUNCH ))
				return false;
			//print ("DBG: situation ok");
			if ((float)vessel.srfSpeed > restVelocityThreshold)
				return false;
			//print ("DBG: speed ok " + vessel.srfSpeed);
			// check if we was on a slope
			if (!slopeCached) {
				//print ("DBG: Slope not cached");
				onASlope = checkSlope ();
				//print ("DBG: onASlope: " + onASlope);
				if (onASlope)
					return false;

				slopeCached = true;
			}

			//print ("DBG: Returning true");
			return true;
		}

		private bool checkSlope() {

			// Borrowed form KER
			try
			{
				CelestialBody mainBody = vessel.mainBody;
				Vector3 rad = (vessel.CoM - mainBody.position).normalized;
				RaycastHit hit;
				if (Physics.Raycast(vessel.CoM, -rad, out hit, Mathf.Infinity, 1 << 15)) // Just "Local Scenery" please
				{
					Vector3 norm = hit.normal;
					norm = norm.normalized; // normalized normal surface vector
					slopeAngle = Math.Abs(90 - Vector3.Angle(Vector3.up, norm));
					return slopeAngle > slopeAngleThreshold;
				}
				else 
					return false; // Let's think we're not on a slope if we're not sure

			}
			catch (Exception ex)
			{
				print ("CGRW: Got exception: " + ex);
				return false;
			}

		}

		private void freeze() {

			print ("CGRW: restoring position and freezing");
			vessel.SetPosition (position);
			vessel.SetRotation (rotation);

			foreach (Part p  in vessel.parts)
				if (p.Rigidbody != null) {
					p.Rigidbody.Sleep ();
					p.Rigidbody.velocity = Vector3.zero;
					p.Rigidbody.angularVelocity = Vector3.zero;
				}
		}

		private void printDebug() {
	
			print ("CGRW: " + vessel.name + ": srfSpeed=" + vessel.srfSpeed);
			print ("CGRW: " + vessel.name + ": slopeAngle=" + slopeAngle);
			print ("CGRW: " + vessel.name + ": standingStill=" + standingStill);
			print ("CGRW: " + vessel.name + ": slopeCached=" + slopeCached);
			print ("CGRW: " + vessel.name + ": situation=" + vessel.SituationString);
			print ("CGRW: " + vessel.name + ": ground contact=" + hasGroundContact);
			print ("CGRW: " + vessel.name + ": needs dampening=" + needsDampening);
			print ("CGRW: " + vessel.name + ": partsOnSleep=" + partsOnSleep);
			print ("CGRW: " + vessel.name + " ****");
		}
	}
}

