﻿using System;
using System.Collections.Generic;
using UnityEngine;
using FerramAerospaceResearch.FARAeroComponents;
using FerramAerospaceResearch.FAREditorSim;
using ferram4;

namespace FerramAerospaceResearch.FARGUI
{
    [KSPAddon(KSPAddon.Startup.EditorAny, false)]
    public class EditorGUI : MonoBehaviour
    {
        const int EDITOR_VOXEL_COUNT = 125000;

        static EditorGUI instance;
        public static EditorGUI Instance
        {
            get { return instance; }
        }

        int _updateRateLimiter = 0;
        bool _updateQueued = true;

        static bool showGUI = false;
        static Rect guiRect;
        public static Rect GUIRect
        {
            get { return guiRect; }
        }
        static ApplicationLauncherButton editorGUIAppLauncherButton;

        VehicleAerodynamics _vehicleAero;
        EditorAeroCenter _aeroCenter;
        EditorAreaRulingOverlay _areaRulingOverlay;
        StaticAnalysisGraphGUI _editorGraph;
        StabilityDerivGUI _stabDeriv;

        FAREditorMode currentMode = FAREditorMode.STATIC;
        private enum FAREditorMode
        {
            STATIC,
            STABILITY,
            AREA_RULING
        }

        private static string[] FAReditorMode_str = 
        {
            "Static Analysis",
            "Data + Stability Derivatives",
            "Transonic Design",
        };


        void Start()
        {
            instance = this;

            _vehicleAero = new VehicleAerodynamics();
            _aeroCenter = new EditorAeroCenter();

            InstantConditionSim instantSim = new InstantConditionSim();
            GUIDropDown<int> flapSettingDropDown = new GUIDropDown<int>(new string[] { "0 (up)", "1 (init climb)", "2 (takeoff)", "3 (landing)" }, new int[] { 0, 1, 2, 3 }, 0);
            GUIDropDown<CelestialBody> celestialBodyDropdown = CreateBodyDropdown();

            _editorGraph = new StaticAnalysisGraphGUI(instantSim, flapSettingDropDown, celestialBodyDropdown);
            _stabDeriv = new StabilityDerivGUI(instantSim, flapSettingDropDown, celestialBodyDropdown);

            _areaRulingOverlay = new EditorAreaRulingOverlay(new Color(0.05f, 0.05f, 0.05f, 0.8f), Color.green, Color.yellow, 10, 5);
            guiRect.height = 500;
            guiRect.width = 650;
            GameEvents.onEditorPartEvent.Add(UpdateGeometryEvent);
            GameEvents.onEditorUndo.Add(ResetEditorEvent);
            GameEvents.onEditorRedo.Add(ResetEditorEvent);
        }

        void OnDestroy()
        {
            GameEvents.onEditorPartEvent.Remove(UpdateGeometryEvent);
            GameEvents.onEditorUndo.Remove(ResetEditorEvent);
            GameEvents.onEditorRedo.Remove(ResetEditorEvent);
            EditorLogic.fetch.Unlock("FAREdLock");
        }

        private void UpdateGeometryEvent(ConstructionEventType type, Part pEvent)
        {
            if (type == ConstructionEventType.PartRotated ||
            type == ConstructionEventType.PartOffset ||
            type == ConstructionEventType.PartAttached ||
            type == ConstructionEventType.PartDetached ||
            type == ConstructionEventType.PartRootSelected)
            {
                UpdateVoxel();
                FARAeroUtil.ResetEditorParts();
            }
        }
        private void ResetEditorEvent(ShipConstruct construct)
        {
            ResetEditor();
        }


        void FixedUpdate()
        {
            if ((object)EditorLogic.RootPart != null)
            {
                if (_vehicleAero.CalculationCompleted)
                {
                    _aeroCenter.UpdateAeroData(_vehicleAero);
                    _editorGraph.UpdateAeroData(_vehicleAero);
                    UpdateCrossSections();
                } 

                if (_updateRateLimiter < 20)
                {
                    _updateRateLimiter++;
                }
                else if (_updateQueued)
                    RecalculateVoxel();
            }

            OnGUIAppLauncherReady();
        }

        #region CenterOfLiftCalcs

        #endregion
        #region voxel
        public static void ResetEditor()
        {
            instance._areaRulingOverlay = new EditorAreaRulingOverlay(new Color(0.05f, 0.05f, 0.05f, 0.8f), Color.green, Color.yellow, 10, 5);
            FARAeroUtil.ResetEditorParts();
            UpdateVoxel();
        }

        public static void UpdateVoxel()
        {
            if (instance._updateRateLimiter > 18)
                instance._updateRateLimiter = 18;
            instance._updateQueued = true;
            //instance._areaRulingOverlay.SetVisibility(false);

        }

        void RecalculateVoxel()
        {
            if (_updateRateLimiter < 20)        //this has been updated recently in the past; queue an update and return
            {
                _updateQueued = true;
                return;
            }
            else                                //last update was far enough in the past to run; reset rate limit counter and clear the queued flag
            {
                _updateRateLimiter = 0;
                _updateQueued = false;
            }

            _vehicleAero.VoxelUpdate(EditorLogic.RootPart.transform.worldToLocalMatrix, EditorLogic.RootPart.transform.localToWorldMatrix, EDITOR_VOXEL_COUNT, EditorLogic.SortedShipList, true);
        }

        void UpdateCrossSections()
        {
            double[] areas = _vehicleAero.GetCrossSectionAreas();
            double[] secondDerivAreas = _vehicleAero.GetCrossSection2ndAreaDerivs();

            double sectionThickness = _vehicleAero.SectionThickness;
            double offset = _vehicleAero.FirstSectionXOffset();

            double[] xAxis = new double[areas.Length];

            double maxValue = 0;
            for (int i = 0; i < areas.Length; i++)
            {
                maxValue = Math.Max(maxValue, areas[i]);
            }

            for (int i = 0; i < xAxis.Length; i++)
            {
                xAxis[i] = (xAxis.Length - i - 1) * sectionThickness + offset;
            }

            _areaRulingOverlay.UpdateAeroData(_vehicleAero.VoxelAxisToLocalCoordMatrix(), xAxis, areas, secondDerivAreas, maxValue);
        }
        #endregion

        #region GUIFunctions
        void OnGUI()
        {
            bool cursorInGUI = false;
            EditorLogic EdLogInstance = EditorLogic.fetch;
            if (showGUI)
            {
                guiRect = GUILayout.Window(this.GetHashCode(), guiRect, OverallSelectionGUI, "FAR Analysis");

                cursorInGUI = guiRect.Contains(FARGUIUtils.GetMousePos());
            }
            if (cursorInGUI)
            {
                EditorTooltip.Instance.HideToolTip();
                EdLogInstance.Lock(false, false, false, "FAREdLock");
            }
            else if (!cursorInGUI)
            {
                EdLogInstance.Unlock("FAREdLock");
            }
        }

        void OverallSelectionGUI(int windowId)
        {
            currentMode = (FAREditorMode)GUILayout.SelectionGrid((int)currentMode, FAReditorMode_str, 3);


            //GUILayout.EndHorizontal();
            if (currentMode == FAREditorMode.STATIC)
                _editorGraph.Display();
            else if (currentMode == FAREditorMode.STABILITY)
                _stabDeriv.Display();
            else if (currentMode == FAREditorMode.AREA_RULING)
            {
                CrossSectionAnalysisGUI();
                DebugVisualizationGUI();
            }


            GUI.DragWindow();
        }

        void DebugVisualizationGUI()
        {
            if (GUILayout.Button("Display Debug Voxels"))
                _vehicleAero.DebugVisualizeVoxels(EditorLogic.RootPart.transform.localToWorldMatrix);
        }

        void CrossSectionAnalysisGUI()
        {
            if (GUILayout.Button("Toggle CrossSections"))
                _areaRulingOverlay.ToggleVisibility();

            //GraphDisplay();
        }
        #endregion

        #region AppLauncher
        public void OnGUIAppLauncherReady()
        {
            if (ApplicationLauncher.Ready && editorGUIAppLauncherButton == null)
            {
                if (EditorDriver.editorFacility == EditorFacility.VAB)
                {
                    editorGUIAppLauncherButton = ApplicationLauncher.Instance.AddModApplication(
                        onAppLaunchToggleOn,
                        onAppLaunchToggleOff,
                        DummyVoid,
                        DummyVoid,
                        DummyVoid,
                        DummyVoid,
                        ApplicationLauncher.AppScenes.VAB,
                        (Texture)GameDatabase.Instance.GetTexture("FerramAerospaceResearch/Textures/icon_button_stock", false));
                }
                else
                {
                    editorGUIAppLauncherButton = ApplicationLauncher.Instance.AddModApplication(
                        onAppLaunchToggleOn,
                        onAppLaunchToggleOff,
                        DummyVoid,
                        DummyVoid,
                        DummyVoid,
                        DummyVoid,
                        ApplicationLauncher.AppScenes.SPH,
                        (Texture)GameDatabase.Instance.GetTexture("FerramAerospaceResearch/Textures/icon_button_stock", false));
                }

            }
        }

        void onAppLaunchToggleOn()
        {
            showGUI = true;
        }

        void onAppLaunchToggleOff()
        {
            showGUI = false;
        }

        void DummyVoid() { }
        #endregion

        #region UtilFuncs
        GUIDropDown<CelestialBody> CreateBodyDropdown()
        {
            CelestialBody[] bodies = FlightGlobals.Bodies.ToArray();
            string[] bodyNames = new string[bodies.Length];
            for (int i = 0; i < bodyNames.Length; i++)
                bodyNames[i] = bodies[i].bodyName;

            int kerbinIndex = 1;
            GUIDropDown<CelestialBody> celestialBodyDropdown = new GUIDropDown<CelestialBody>(bodyNames, bodies, kerbinIndex);
            return celestialBodyDropdown;
        }

        #endregion
    }
}
