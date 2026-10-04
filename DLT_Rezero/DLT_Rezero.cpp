#include <opencv2/opencv.hpp>
#include <opencv2/core/core.hpp>
#include <opencv2/calib3d/calib3d.hpp>
#include <iostream>


extern "C" __declspec(dllexport) void DLT(double* worldPoints, double* imagePoints, int numPoints, double* projectionMatrix)
{
    if (numPoints < 6) {
        std::cout << "Invalid number of points provided for DLT." << std::endl;
        return;
    }

    cv::Mat worldPointsMat(numPoints, 3, CV_64F, worldPoints);
    cv::Mat imagePointsMat(numPoints, 2, CV_64F, imagePoints);
    cv::Mat projectionMatrixMat;

    cv::Mat A(2 * numPoints, 11, CV_64F);
    for (int i = 0; i < numPoints; ++i) {
        double X = worldPointsMat.at<double>(i, 0);
        double Y = worldPointsMat.at<double>(i, 1);
        double Z = worldPointsMat.at<double>(i, 2);

        double u = imagePointsMat.at<double>(i, 0);
        double v = imagePointsMat.at<double>(i, 1);

        A.at<double>(2 * i, 0) = X;
        A.at<double>(2 * i, 1) = Y;
        A.at<double>(2 * i, 2) = Z;
        A.at<double>(2 * i, 3) = 1.0;
        A.at<double>(2 * i, 4) = 0.0;
        A.at<double>(2 * i, 5) = 0.0;
        A.at<double>(2 * i, 6) = 0.0;
        A.at<double>(2 * i, 7) = 0.0;
        A.at<double>(2 * i, 8) = -u * X;
        A.at<double>(2 * i, 9) = -u * Y;
        A.at<double>(2 * i, 10) = -u * Z;
        //A.at<double>(2 * i, 11) = -u;

        A.at<double>(2 * i + 1, 0) = 0.0;
        A.at<double>(2 * i + 1, 1) = 0.0;
        A.at<double>(2 * i + 1, 2) = 0.0;
        A.at<double>(2 * i + 1, 3) = 0.0;
        A.at<double>(2 * i + 1, 4) = X;
        A.at<double>(2 * i + 1, 5) = Y;
        A.at<double>(2 * i + 1, 6) = Z;
        A.at<double>(2 * i + 1, 7) = 1.0;
        A.at<double>(2 * i + 1, 8) = -v * X;
        A.at<double>(2 * i + 1, 9) = -v * Y;
        A.at<double>(2 * i + 1, 10) = -v * Z;
        //A.at<double>(2 * i + 1, 11) = -v;
    }
    cv::Mat k(2 * numPoints, 1, CV_64F);
    for (int i = 0; i < numPoints; ++i) {
        double u = imagePointsMat.at<double>(i, 0);
        double v = imagePointsMat.at<double>(i, 1);
        k.at<double>(2 * i, 0) = u;
        k.at<double>(2 * i + 1, 0) = v;
    }

    cv::Mat gt;
    //DECOMP_NORMAL이 안되면 다른 DECOMP flag 사용. ex) DECOMP_SVD, DECOMP_LU, DECOMP_QR
    cv::solve(A, k, gt, cv::DECOMP_SVD);
    //gt가 가지는 값은 결국 x. 즉, x는 11 parameter
    
    memcpy(projectionMatrix, gt.data, sizeof(double) * 11);
}

//extern "C" __declspec(dllexport) void projectPoints(double* worldPoints, double* projectionMatrix, double* rtMatrix, double* resultPoints, float camPos)
//{
//    // Convert input arrays to cv::Mat
//    cv::Mat worldPointsMat(6, 3, CV_64F, worldPoints);
//    cv::Mat rtMatrixMat(3, 4, CV_64F, rtMatrix);
//    cv::Mat projectionMatrixMat(3, 4, CV_64F, projectionMatrix);
//    int numPoints = worldPointsMat.rows;
//
//    cv::Mat projectedPointsMat(numPoints, 2, CV_64F);
//
//    for (int i = 0; i < numPoints; ++i) {
//        cv::Mat worldPoint = worldPointsMat.row(i).t();
//        cv::Mat homogeneousWorldPoint;
//        cv::vconcat(worldPoint, cv::Mat::ones(1, 1, CV_64F), homogeneousWorldPoint);
//
//        cv::Mat projectedPoint = projectionMatrixMat * homogeneousWorldPoint;
//        projectedPoint /= projectedPoint.at<double>(2); // Normalize by dividing by the third component (homogeneous coordinate)
//
//        double u = projectedPoint.at<double>(0);
//        double v = projectedPoint.at<double>(1);
//
//        // Convert OpenCV coordinates to Unity coordinates
//        double unity_u = u; // X coordinates remain the same
//        double unity_v = v;//camPos - v; // Flip Y coordinates // 
//        
//        projectedPointsMat.at<double>(i, 0) = unity_u;
//        projectedPointsMat.at<double>(i, 1) = unity_v;
//    }
//
//    // Copy the result to the output array using memcpy
//    memcpy(resultPoints, projectedPointsMat.data, sizeof(double) * numPoints * 2);
//}

//#include <opencv2/opencv.hpp>
//#include <opencv2/core/core.hpp>
//#include <opencv2/calib3d/calib3d.hpp>
//#include <iostream>
//
//
//extern "C" __declspec(dllexport) void DLT(double* worldPoints, double* imagePoints, int numPoints, double* projectionMatrix, double* rtMatrix, double* KMatrix)
//{
//    if (numPoints < 6) {
//        std::cout << "Invalid number of points provided for DLT." << std::endl;
//        return;
//    }
//
//    cv::Mat worldPointsMat(numPoints, 3, CV_64F, worldPoints);
//    cv::Mat imagePointsMat(numPoints, 2, CV_64F, imagePoints);
//    cv::Mat projectionMatrixMat;
//
//    cv::Mat A(2 * numPoints, 12, CV_64F);
//    for (int i = 0; i < numPoints; ++i) {
//        double X = worldPointsMat.at<double>(i, 0);
//        double Y = worldPointsMat.at<double>(i, 1);
//        double Z = worldPointsMat.at<double>(i, 2);
//
//        double u = imagePointsMat.at<double>(i, 0);
//        double v = imagePointsMat.at<double>(i, 1);
//
//        A.at<double>(2 * i, 0) = X;
//        A.at<double>(2 * i, 1) = Y;
//        A.at<double>(2 * i, 2) = Z;
//        A.at<double>(2 * i, 3) = 1.0;
//        A.at<double>(2 * i, 4) = 0.0;
//        A.at<double>(2 * i, 5) = 0.0;
//        A.at<double>(2 * i, 6) = 0.0;
//        A.at<double>(2 * i, 7) = 0.0;
//        A.at<double>(2 * i, 8) = -u * X;
//        A.at<double>(2 * i, 9) = -u * Y;
//        A.at<double>(2 * i, 10) = -u * Z;
//        A.at<double>(2 * i, 11) = -u;
//
//        A.at<double>(2 * i + 1, 0) = 0.0;
//        A.at<double>(2 * i + 1, 1) = 0.0;
//        A.at<double>(2 * i + 1, 2) = 0.0;
//        A.at<double>(2 * i + 1, 3) = 0.0;
//        A.at<double>(2 * i + 1, 4) = X;
//        A.at<double>(2 * i + 1, 5) = Y;
//        A.at<double>(2 * i + 1, 6) = Z;
//        A.at<double>(2 * i + 1, 7) = 1.0;
//        A.at<double>(2 * i + 1, 8) = -v * X;
//        A.at<double>(2 * i + 1, 9) = -v * Y;
//        A.at<double>(2 * i + 1, 10) = -v * Z;
//        A.at<double>(2 * i + 1, 11) = -v;
//    }
//
//    cv::Mat u, w, vt;
//    cv::SVDecomp(A, w, u, vt);
//
//    cv::Mat solution = vt.row(vt.rows - 1);
//
//    //P matrix = K와 RT행렬이 결합되어 있는 투영 행렬
//    projectionMatrixMat = cv::Mat(3, 4, CV_64F, projectionMatrix);
//    for (int i = 0; i < 12; ++i) {
//        projectionMatrixMat.at<double>(i / 4, i % 4) = solution.at<double>(0, i);
//    }
//
//    cv::Mat K, R;
//    cv::RQDecomp3x3(projectionMatrixMat(cv::Rect(0, 0, 3, 3)), K, R);
//
//    // Normalize K to ensure the bottom-right value is 1 -> 여기서 동차좌표계로 계산되는 듯 한데 위의 값은 동차좌표가 아님 -> 그럼 굳이 이 과정을 할 이유가 있나?
//    K /= K.at<double>(2, 2);
//
//    // Extract translation vector from the last column of the projection matrix
//    cv::Mat T = projectionMatrixMat.col(3).clone();
//    //T = K.inv() * T;  // Adjust the translation vector with the inverse of K
//
//    // Combine R and T into RT matrix
//    cv::Mat rtMatrixMat(3, 4, CV_64F);
//    R.copyTo(rtMatrixMat(cv::Rect(0, 0, 3, 3)));
//    T.copyTo(rtMatrixMat.col(3));
//
//
//    // Copy the RT matrix to the output array
//    memcpy(rtMatrix, rtMatrixMat.data, sizeof(double) * 12);
//
//    memcpy(KMatrix, K.data, sizeof(double) * 9);
//
//    memcpy(projectionMatrix, projectionMatrixMat.data, sizeof(double) * 12);
//}
//
//extern "C" __declspec(dllexport) void projectPoints(double* worldPoints, double* projectionMatrix, double* rtMatrix, double* resultPoints, float camPos)
//{
//    // Convert input arrays to cv::Mat
//    cv::Mat worldPointsMat(6, 3, CV_64F, worldPoints);
//    cv::Mat rtMatrixMat(3, 4, CV_64F, rtMatrix);
//    cv::Mat projectionMatrixMat(3, 4, CV_64F, projectionMatrix);
//    int numPoints = worldPointsMat.rows;
//
//    cv::Mat projectedPointsMat(numPoints, 2, CV_64F);
//
//    for (int i = 0; i < numPoints; ++i) {
//        cv::Mat worldPoint = worldPointsMat.row(i).t();
//        cv::Mat homogeneousWorldPoint;
//        cv::vconcat(worldPoint, cv::Mat::ones(1, 1, CV_64F), homogeneousWorldPoint);
//
//        cv::Mat projectedPoint = projectionMatrixMat * homogeneousWorldPoint;
//        projectedPoint /= projectedPoint.at<double>(2); // Normalize by dividing by the third component (homogeneous coordinate)
//
//        double u = projectedPoint.at<double>(0);
//        double v = projectedPoint.at<double>(1);
//
//        // Convert OpenCV coordinates to Unity coordinates
//        double unity_u = u; // X coordinates remain the same
//        double unity_v = v;//camPos - v; // Flip Y coordinates // 
//
//        projectedPointsMat.at<double>(i, 0) = unity_u;
//        projectedPointsMat.at<double>(i, 1) = unity_v;
//    }
//
//    // Copy the result to the output array using memcpy
//    memcpy(resultPoints, projectedPointsMat.data, sizeof(double) * numPoints * 2);
//}