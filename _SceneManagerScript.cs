using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
//using System.Text.Json;
using System.IO;
using Newtonsoft.Json;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;


public class _SceneManagerScript : MonoBehaviour
{
    //create visualization variables
    private Camera cam;
    [SerializeField]
    private Vector3 objPos;
    public GameObject prefab;
    public GameObject pottedPlantPrefab;
    public GameObject personPrefab;
    public GameObject trafficLightPrefab;
    public float threshold = 0.5f;

    static int frameCounter = 1;

    public string METADATA_FILE_NAME = "Assets/metadata.txt";
    public string OBJECT_DETECTION_JSON = "./Assets/out.json";
    public static string DEPTH_DATA_JSON = "./Assets/_output_data.dat";

    //create dataframe variables
    Frames frames;
    Dictionary<string, TrackObjectData> current = new Dictionary<string, TrackObjectData>();
    Dictionary<string, TrackObjectData> previous = new Dictionary<string, TrackObjectData>();  
    Dictionary<string, List<TrackObjectData>> allObjects = new Dictionary<string, List<TrackObjectData>>();
    List<string> notInFrame = new List<string>();
    Dictionary<string, TrackObjectData> oldXYZ = new Dictionary<string, TrackObjectData>();
    Dictionary<string, Vector3> newXYZ = new Dictionary<string, Vector3>();

    FileStream depth_file = new FileStream(DEPTH_DATA_JSON, FileMode.Open, FileAccess.Read);
    static bool flag = false;

    public Slider frameRateSlider;
    VideoPlayer videoPlayer;

    [Range(0.001f, 1f)]
    public float lerpspeed;
    
    MercatorProjection mercator = new MercatorProjection(0);

    // Start is called before the first frame update
    void Start()
    {
        //set player, slider, and camera up
        videoPlayer = (VideoPlayer)FindObjectOfType(typeof(VideoPlayer));
        frameRateSlider = (Slider)FindObjectOfType(typeof(Slider));
        frameRateSlider.onValueChanged.AddListener(delegate {ValueChangeCheck();});
        cam = Camera.main;

        //read json for YOLO bounding boxes
        using(StreamReader r = new StreamReader(OBJECT_DETECTION_JSON)){
            string json = r.ReadToEnd();
            frames = JsonConvert.DeserializeObject<Frames>(json);
        }

        //delete old metadata file if it exists
        if(File.Exists(METADATA_FILE_NAME)){
            File.Delete(METADATA_FILE_NAME);
        }
    }

    // Update is called once per frame
    void FixedUpdate()
    {
        oldXYZ.Clear();
        newXYZ.Clear();

        //filter dataframe to contain objects with confidence > threshold
        current = frames.frames[frameCounter.ToString()].objects.Where(o => o.Value.score > threshold).ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

        using (StreamWriter sw = new StreamWriter(METADATA_FILE_NAME, true)){
            sw.WriteLine($"{frames.frames[frameCounter.ToString()].est_lat} {frames.frames[frameCounter.ToString()].est_lon}");
        }

        //get all keys in previous and not in current
        List<string> previousKeys = previous.Keys.ToList();
        List<string> currentKeys = current.Keys.ToList();
        notInFrame = previousKeys.Except(currentKeys).ToList();
        
        //destroy objects no longer in frame
        foreach(var deleteKey in notInFrame) {
            if (previous[deleteKey].NewObj != null) {
                    Destroy(previous[deleteKey].NewObj);
            }
        }
        notInFrame.Clear();
        
        //loop through id of all objects in the frame
        foreach (var key in current.Keys) {

            //calculate bounding boxes and depth
            float x1 = current[key].tlbr[0];
            float x2 = current[key].tlbr[2];
            float y1 = current[key].tlbr[1];
            float y2 = current[key].tlbr[3];
            // scale
            int w = 640;
            int h = 192;
            float x_ratio = (float)640/1280;
            float y_ratio = (float)192/720;
            int mid_x = Convert.ToInt32(Math.Floor(((x2 + x1)*x_ratio)/2));
            int mid_y = Convert.ToInt32(Math.Floor(((y2 + y1)*y_ratio)/2));
            int depth_lowest_byte =  ( ( (w * h * (frameCounter - 1)) + (w * mid_y) + mid_x ) * 2 ) + 4;

            depth_file.Seek(depth_lowest_byte, SeekOrigin.Begin);
        
            ushort data = (ushort) depth_file.ReadByte();
            ushort temp = (ushort) (depth_file.ReadByte() << 8);
            data |= temp;

            float f = BitConverter.Int32BitsToSingle(((data&0x8000)<<16) | (((data&0x7c00)+0x1C000)<<13) | ((data&0x03FF)<<13));
            Vector3 point = new Vector3(mid_x * Screen.width / 1280 * 2, Screen.height - (mid_y * Screen.height / 192) , f);
            Vector3 coordinates = cam.ScreenToWorldPoint(point);

            //get x,y coordinates of the camera
            double cam_coord_lat = frames.frames[frameCounter.ToString()].est_lat;
            double cam_coord_lon = frames.frames[frameCounter.ToString()].est_lon;
            //get the x,y coordinates of the camera 
            Tuple<double, double> cam_coord = mercator.Project(MercatorProjection.DdmToDd(cam_coord_lat), MercatorProjection.DdmToDd(cam_coord_lon));

            //set the real world GPS coordinates (add x,y coordinates of cam to object)
            current[key].GPSCoordinates = mercator.InverseProject(-(coordinates.x + cam_coord.Item1), coordinates.z + cam_coord.Item2, 0.00001);
            
            //if object appeared in both the previous and current frame
            if(previous.ContainsKey(key)){ 
                //set current's object to be the same as previous
                current[key].NewObj = previous[key].NewObj;
                
                //set XYZ for interpolation
                oldXYZ.Add(key, previous[key]);
                newXYZ.Add(key, coordinates);
            }
            //all other objects (create and add to allObjects)
            else {
                //GameObject newObj = CreateObject(current[key].Class, point);
                current[key].NewObj = CreateObject(current[key].Class, point);

                //this part (format of allObjects) will change once structure of metadata is decided
                if (!allObjects.ContainsKey(key)) {
                    allObjects[key] = new List<TrackObjectData>();
                }
                allObjects[key].Add(current[key]);
            }
        }
        
        //set previous to current in anticipation of next frame
        previous = current;
        
        frameCounter++; //increment frame counter
    }

    void Update() {
        foreach(var key in oldXYZ.Keys) {
            Vector3 old_val = oldXYZ[key].NewObj.transform.position;
            Vector3 new_val = newXYZ[key];
            Vector3 interpolatedPosition = Vector3.Lerp(old_val, new_val, lerpspeed);
            oldXYZ[key].NewObj.transform.position = interpolatedPosition;
        }
    }

    void OnDestroy() {
       // this.stream.Close();
    }

    public void ValueChangeCheck()
    {
        if (!flag) {
            frameCounter = (int)frameRateSlider.value;
            videoPlayer.frame = frameCounter;
        }
    }
    /** Helper method that creates gameobject by its classtype using ScreenToWorldPoint
    **/
    private GameObject CreateObject(string classType, Vector3 point){
        GameObject newObj;
        switch(classType) {
            case "potted plant":
                newObj = Instantiate(pottedPlantPrefab, cam.ScreenToWorldPoint(point), Quaternion.identity);
                break;
            case "person":
                newObj = Instantiate(personPrefab, cam.ScreenToWorldPoint(point), Quaternion.identity);
                break;
            case "traffic light":
                newObj = Instantiate(trafficLightPrefab, cam.ScreenToWorldPoint(point), Quaternion.identity);
                break;
            default:
                newObj = Instantiate(prefab, cam.ScreenToWorldPoint(point), Quaternion.identity);
                break;
        }
        return newObj;
    }

    public class Frames {
        public Dictionary<string, FrameData> frames { get; set; }
    }

    public class FrameData{
        public Dictionary<string, TrackObjectData> objects{get; set;}    
        public float est_lat{get; set;}
        public float est_lon{get; set;}
        public float heading{get; set;}
    }

    public class TrackObjectData{
        //[JsonProperty(PropertyName="class")qgi]
        public string Class {get; set;}
        public float[] tlbr {get; set;} 
        public float score{get; set;}
        [JsonIgnore]
        public GameObject NewObj {get; set;}
        [JsonIgnore]
        public Tuple<double, double> GPSCoordinates;
    }

}