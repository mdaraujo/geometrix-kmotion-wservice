#include <iostream>

#include <opencv2/core/core.hpp>
#include <opencv2/flann/flann.hpp>
#include <opencv2/imgproc/imgproc.hpp>
#include <opencv2/calib3d/calib3d.hpp>
#include <opencv2/features2d/features2d.hpp>
#include <opencv2/imgcodecs/imgcodecs.hpp>
#include <opencv2/highgui/highgui.hpp>
#include <opencv2/xfeatures2d/nonfree.hpp>


using namespace std;
using namespace cv;
using namespace cv::xfeatures2d;


// window names
char* windowCameraName = "Camera";
char* windowCaptureName = "Captured Image";
char* windowTresholdName = "Threshold Output";
char* windowCroppedName = "Cropped Image";
char* windowContoursName = "Contours";

//global variables
vector<Mat> capturedImgs;
Mat imgCropped;
Mat imgCapture;
Mat imgCaptureGray;
int thresh = 100;
int max_thresh = 255;
RNG rng(12345);

bool showMatches = true; // for debug


// variables used on match function
vector<KeyPoint> keypoints_1, keypoints_2;
Mat descriptors_1, descriptors_2;
FlannBasedMatcher matcher;
vector< DMatch > matches;
vector< DMatch > good_matches;

// function headers
void thresh_callback(int, void*);
int match(Mat img_1, Mat img_2, string img_id);


///////////////////////////////////////////////////////////////////////////////////////////////////
int main() {
	VideoCapture capWebcam(0);            // declare a VideoCapture object and associate to webcam, 0 => use 1st webcam

	if (!capWebcam.isOpened()) {                                // check if VideoCapture object was associated to webcam successfully
		cout << "error: capWebcam not accessed successfully\n\n";      // if not, print error message to std out
		return(0);                                                          // and exit program
	}

	if (!showMatches)
	{	
		// show empty images
		Mat imgEmpty = Mat::zeros(250, 250, CV_8UC3);
		imshow(windowCameraName, imgEmpty);
	}
	
	Mat imgCamera;
	char keyPressed = 0;
	bool capturing = false;

	while (keyPressed != 27 && capWebcam.isOpened()) {            // until the Esc key is pressed or webcam connection is lost
		bool blnFrameReadSuccessfully = capWebcam.read(imgCamera);            // get next frame

		if (!blnFrameReadSuccessfully || imgCamera.empty()) {                 // if frame not read successfully
			cout << "error: frame not read from webcam\n";                 // print error message to std out
			break;                                                              // and jump out of while loop
		}

		//cv::namedWindow("imgCamera", CV_WINDOW_NORMAL); // default is AOUTOZIE wich not permit the user to resize the window
		if (showMatches)
		{
			imshow(windowCameraName, imgCamera);
		}

		if (keyPressed == 32) // 32=space
		{
			capturing = true;
			imgCapture = imgCamera.clone();
			imshow(windowCaptureName, imgCapture);

			// Convert image to gray and blur it
			cvtColor(imgCapture, imgCaptureGray, CV_BGR2GRAY);
			blur(imgCaptureGray, imgCaptureGray, Size(3, 3));

			createTrackbar(" Threshold:", windowCaptureName, &thresh, max_thresh, thresh_callback);
			thresh_callback(0, 0);
		}

		if (keyPressed == 13 && capturing) // 13=enter
		{
			capturedImgs.push_back(imgCropped);
			capturing = false;
			thresh = 100;

			cvDestroyWindow(windowCaptureName);
			cvDestroyWindow(windowTresholdName);
			cvDestroyWindow(windowCroppedName);
			cvDestroyWindow(windowContoursName);
		}

		for (size_t i = 0; i < capturedImgs.size(); i++)
		{
			match(imgCamera, capturedImgs.at(i), to_string(i + 1));
		}

		keyPressed = waitKey(1);        // delay (in ms) and get key press, if any
	}   // end while

	cvDestroyWindow(windowCameraName);
	
	for (size_t i = 0; i < capturedImgs.size(); i++)
	{
		imshow("Image " + to_string(i + 1), capturedImgs.at(i));
	}

	waitKey(0);
	return(0);
}

/** @function thresh_callback */
void thresh_callback(int, void*)
{
	Mat threshold_output;
	vector<vector<Point>> contours;
	vector<Vec4i> hierarchy;

	// Detect edges using Threshold
	threshold(imgCaptureGray, threshold_output, thresh, 255, THRESH_BINARY);
	imshow(windowTresholdName, threshold_output);
	// Find contours
	findContours(threshold_output, contours, hierarchy, CV_RETR_EXTERNAL, CV_CHAIN_APPROX_SIMPLE, Point(0, 0));

	// Approximate contours to polygons + get bounding rects and circles
	vector<vector<Point>> contours_poly(contours.size());
	vector<Rect> boundRect(contours.size());
	vector<Point2f> center(contours.size());
	vector<float> radius(contours.size());

	int maxBoundRect = 0;
	double maxArea = 0.0;
	for (int i = 0; i < contours.size(); i++)
	{
		approxPolyDP(Mat(contours[i]), contours_poly[i], 3, true);
		boundRect[i] = boundingRect(Mat(contours_poly[i]));
		minEnclosingCircle((Mat)contours_poly[i], center[i], radius[i]);

		double area = contourArea(contours[i]);
		if (area > maxArea) {
			maxArea = area;
			maxBoundRect = i;
		}
	}

	imgCropped = imgCapture(boundRect[maxBoundRect]);
	imshow(windowCroppedName, imgCropped);


	// Draw polygonal contour + bonding rects + circles
	Mat drawing = Mat::zeros(threshold_output.size(), CV_8UC3);
	for (int i = 0; i< contours.size(); i++)
	{
		Scalar color = Scalar(rng.uniform(0, 255), rng.uniform(0, 255), rng.uniform(0, 255));
		drawContours(drawing, contours_poly, i, color, 1, 8, vector<Vec4i>(), 0, Point());
		rectangle(drawing, boundRect[i].tl(), boundRect[i].br(), color, 2, 8, 0);
		circle(drawing, center[i], (int)radius[i], color, 2, 8, 0);
	}

	// Show in a window
	imshow(windowContoursName, drawing);
}


int match(Mat img_1, Mat img_2, string img_id)
{
	if (!img_1.data || !img_2.data)
	{
		printf(" --(!) Error reading images \n");
		system("pause");
		exit(EXIT_FAILURE);
	}

	//-- Step 1: Detect the keypoints using SURF Detector
	keypoints_1.clear();
	keypoints_2.clear();
	int minHessian = 400;
	Ptr<SURF> detector = SURF::create(minHessian);
	detector->detect(img_1, keypoints_1);
	detector->detect(img_2, keypoints_2);

	//-- Step 2: Calculate descriptors (feature vectors)
	Ptr<SURF> extractor = SURF::create();
	extractor->compute(img_1, keypoints_1, descriptors_1);
	extractor->compute(img_2, keypoints_2, descriptors_2);

	//-- Step 3: Matching descriptor vectors using FLANN matcher
	matches.clear();
	matcher.match(descriptors_1, descriptors_2, matches);

	double max_dist = 0; double min_dist = 100;

	//-- Quick calculation of max and min distances between keypoints
	for (int i = 0; i < descriptors_1.rows; i++)
	{
		double dist = matches[i].distance;
		if (dist < min_dist) min_dist = dist;
		if (dist > max_dist) max_dist = dist;
	}

	//printf("-- Max dist : %f \n", max_dist);
	//printf("-- Min dist : %f \n", min_dist);

	//-- Draw only "good" matches (i.e. whose distance is less than 2*min_dist,
	//-- or a small arbitary value ( 0.02 ) in the event that min_dist is very
	//-- small)
	//-- PS.- radiusMatch can also be used here.
	
	good_matches.clear();
	for (int i = 0; i < descriptors_1.rows; i++)
	{
		if (matches[i].distance <= max(2 * min_dist, 0.02))
		{
			good_matches.push_back(matches[i]);
		}
	}

	int nMatches = (int)good_matches.size();

	if (showMatches)
	{
		//-- Draw only "good" matches
		Mat img_matches;
		drawMatches(img_1, keypoints_1, img_2, keypoints_2,
			good_matches, img_matches, Scalar::all(-1), Scalar::all(-1),
			vector<char>(), DrawMatchesFlags::NOT_DRAW_SINGLE_POINTS);



		//-- Show detected matches
		string window_name = "Image " + img_id;
		imshow(window_name, img_matches);
		//resizeWindow(window_name, 600, 600);
	}

	//for (int i = 0; i < nMatches; i++)
	//{
	//	printf("-- Good Match [%d] Keypoint 1: %d  -- Keypoint 2: %d  \n", i, good_matches[i].queryIdx, good_matches[i].trainIdx);
	//}

	cout << "Image " << img_id << " - Good Matches: " << to_string(nMatches) << endl;

	return nMatches;
}