#[compute]
#version 450

// Invocations in the (x, y, z) dimension
layout(local_size_x = 8, local_size_y = 8, local_size_z = 8) in;

// A binding to the buffer we create in our script
layout(set = 0, binding = 0, std430) restrict buffer CurveBuffer {
    float curvePoints[];
}
curve_buffer;

// A binding to the buffer we create in our script
layout(set = 0, binding = 1, std430) restrict buffer GridBuffer {
    mat3 gridPoints[];
}
grid_buffer;

// The code we want to execute in each invocation
void main() {
    // gl_GlobalInvocationID.x uniquely identifies this invocation across all work groups
}