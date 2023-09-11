using System;

/// Class for Mercator projection of latitude, longitude coordinates from an ellipsoid into Cartesian (x, y) rectangular coordinates
public class MercatorProjection {
    public const double Deg2Rad = Math.PI / 180.0d;
    public const double Rad2Deg = 180.0d / Math.PI;
    public double CentralMeridian { get; set; } = 0;
    public double Semimajor { get; set; } = 6378137.0d;
    public double Semiminor { get; set; } = 6356752.31424518d;
    public double Eccentricity { get; set; }
    
    #region Constructors
    public MercatorProjection() {
        // √(1 - b²/a²)
        Eccentricity = Math.Sqrt(1 - (Semiminor * Semiminor) / (Semimajor * Semimajor));
    }

    public MercatorProjection(double centralMeridian) : this() {
        CentralMeridian = centralMeridian;
    }

    public MercatorProjection(double semimajor, double semiminor) : this() {
        Semimajor = semimajor;
        Semiminor = semiminor;
    }

    public MercatorProjection(double semimajor, double semiminor, double centralMeridian) : this(semimajor, semiminor) {
        CentralMeridian = centralMeridian;
    }
    #endregion

    /// <summary>
    /// Takes a latitude, longitude in decimal degrees (DD) and converts it to an x, y coordinate with an x-offset based on the central meridian
    /// </summary>
    /// <param name="lat">latitude in DD</param>
    /// <param name="lon">longitude in DD</param>
    /// <returns>x, y coordinates with x-offset based on the central meridian</returns>
    // Adapted from Snyder, J. P. (1987). Map Projections: A Working Manual. US Geological Survey Professional Paper 1395. doi:10.3133/pp1395 
    public Tuple<double, double> Project(double lat, double lon) {
        double sinPhi = Math.Sin(Deg2Rad * lat);

        double x = Semimajor * Deg2Rad * (lon - CentralMeridian);
        double y = Semimajor / 2d * Math.Log(((1 + sinPhi) / (1 - sinPhi)) 
                                            * Math.Pow((1 - Eccentricity * sinPhi) / (1 + Eccentricity * sinPhi), Eccentricity));

        return new Tuple<double, double>(x, y);
    }

    /// <summary>
    /// Converts x, y cartesian coordinates back into latitude, longitude in DD. Latitude is calculated iteratively until a specified
    /// precision threshold is reached.
    /// </summary>
    /// <param name="x"></param>
    /// <param name="y"></param>
    /// <param name="epsilon">Precision threshold</param>
    /// <returns></returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when precision threshold epsilon is nonpositive</exception>
    // Adapted from Snyder, J. P. (1987). Map Projections: A Working Manual. US Geological Survey Professional Paper 1395. doi:10.3133/pp1395 
    public Tuple<double, double> InverseProject(double x, double y, double epsilon) {
        if (epsilon <= 0) throw new ArgumentOutOfRangeException("Epsilon must be positive");

        double lon = x / Semimajor + CentralMeridian;

        double t = Math.Exp(-y / Semimajor);
        // First trial
        // ϕ₀ = π/2 - 2arctan(t)
        double oldLat = Math.PI/2.0d - 2*Math.Atan(t);
        
        // Next iteration
        double sinPhi = Math.Sin(oldLat);
        // ϕ = π/2 - 2arctan(t[(1 - esinϕ) / (1 + esinϕ)]^(e/2))
        double newLat = Math.PI/2.0d - 2.0d * Math.Atan(t * Math.Pow((1 - Eccentricity * sinPhi) / (1 + Eccentricity * 
            sinPhi), Eccentricity / 2.0d));
        
        // Continue to iterate until precision threshold has been reached
        while (Math.Abs(newLat - oldLat) > epsilon) {
            oldLat = newLat; // Prepare for new trial
            
            // Calculate next trial
            sinPhi = Math.Sin(oldLat);
            newLat = Math.PI/2.0d - 2.0d * Math.Atan(t * Math.Pow((1 - Eccentricity * sinPhi) / (1 + Eccentricity * 
                sinPhi), Eccentricity / 2.0d));
        }

        return new Tuple<double, double>(Rad2Deg * oldLat, Rad2Deg * lon);
    }

    /// <summary>
    /// Converts decimal degree-minutes (ddm) to decimal degrees (dd)
    /// </summary>
    /// <param name="ddm">Coordinate in ddm</param>
    /// <returns>Decimal degrees for coordinate</returns>
    public static double DdmToDd(double ddm) {
        double degrees = Math.Floor(ddm / 100.0d);
        double mins = ddm - degrees * 100.0d;
        return degrees + mins / 60.0d;
    }
}
