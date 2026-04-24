window.initMapPicker = (dotnetRef) => {
    const defaultLocation = { lat: 23.0225, lng: 72.5714 }; // Ahmedabad default

    const map = new google.maps.Map(document.getElementById("map"), {
        center: defaultLocation,
        zoom: 13
    });

    let marker = new google.maps.Marker({
        position: defaultLocation,
        map: map,
        draggable: true
    });

    // Click on map
    map.addListener("click", (e) => {
        const lat = e.latLng.lat();
        const lng = e.latLng.lng();

        marker.setPosition(e.latLng);

        dotnetRef.invokeMethodAsync("SetLocation", lat, lng);
    });

    // Drag marker
    marker.addListener("dragend", (e) => {
        const lat = e.latLng.lat();
        const lng = e.latLng.lng();

        dotnetRef.invokeMethodAsync("SetLocation", lat, lng);
    });
};